using System.Collections.ObjectModel;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// The alert loop.
///
/// <para>Alerts arrive by two routes that overlap on purpose: a push,
/// which is fast but not guaranteed, and a poll, which is slower but
/// always eventually right. Neither is trusted alone. The poll is what
/// makes the app dependable — it covers Windows and macOS entirely, and
/// on mobile it catches whatever FCM dropped while the handset was in a
/// tunnel — and the push is what makes it quick when it works.</para>
///
/// <para>Because the same alert can arrive twice, everything funnels
/// through <see cref="AdmitAsync"/>, which is the only place an alert
/// becomes active and the only place the alarm is started. Alerts are
/// keyed by id, so a push and a poll carrying the same alert produce one
/// entry and one alarm.</para>
///
/// <para>One kind that arrives on this loop is not an alert at all. A
/// <see cref="HandAlert.KindDeviceRemoved"/> notice is Reach telling
/// this handset it has been taken off the rota, and it is intercepted at
/// the door and turned into a sign-out — see
/// <see cref="HandleRemovalNoticeAsync"/>.</para>
/// </summary>
public sealed class AlertService : IAlertService, IDisposable
{
	private readonly IReachClient _reach;
	private readonly IConfigurationService _configuration;
	private readonly IAlertAlarm _alarm;
	private readonly IPlatformAlertPresenter _presenter;
	private readonly IUiDispatcher _dispatcher;

	private readonly SemaphoreSlim _gate = new(1, 1);

	/// <summary>
	/// Ids this handset has already dealt with, so an alert acknowledged
	/// on one route is not re-admitted by the other before the server has
	/// caught up. Bounded — see <see cref="Remember"/>.
	/// </summary>
	private readonly HashSet<long> _handled = [];
	private readonly Queue<long> _handledOrder = new();

	private CancellationTokenSource? _pollLoop;
	private bool _disposed;

	public AlertService(
		IReachClient reach,
		IConfigurationService configuration,
		IAlertAlarm alarm,
		IPlatformAlertPresenter presenter,
		IUiDispatcher dispatcher)
	{
		_reach = reach ?? throw new ArgumentNullException(nameof(reach));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_alarm = alarm ?? throw new ArgumentNullException(nameof(alarm));
		_presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
	}

	public ObservableCollection<HandAlert> Active { get; } = [];

	public event EventHandler<AuthenticationLostEventArgs>? AuthenticationLost;

	public async Task StartAsync()
	{
		if (_pollLoop is not null)
		{
			return;
		}

		var cts = new CancellationTokenSource();
		_pollLoop = cts;

		var interval = TimeSpan.FromSeconds(_configuration.GetReachConfiguration().PollSeconds);

		_ = Task.Run(
			async () =>
			{
				// Poll immediately, then on the interval. A responder who has
				// just opened the app should not wait a full cycle to find out
				// they are needed.
				while (!cts.IsCancellationRequested)
				{
					try
					{
						await PollAsync(cts.Token).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						return;
					}
					catch (Exception ex)
					{
						// The loop must survive anything. A poll that throws
						// and kills the timer is a handset that has silently
						// stopped listening, which is the failure this whole
						// design exists to avoid.
						Log.Error(ex, "Alert poll failed");
					}

					try
					{
						await Task.Delay(interval, cts.Token).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
						return;
					}
				}
			},
			cts.Token);

		Log.Information("Alert polling started at {Interval}s", interval.TotalSeconds);

		await Task.CompletedTask.ConfigureAwait(false);
	}

	public async Task StopAsync()
	{
		var cts = _pollLoop;
		_pollLoop = null;

		if (cts is not null)
		{
			await cts.CancelAsync().ConfigureAwait(false);
			cts.Dispose();
		}

		await _alarm.StopAsync().ConfigureAwait(false);

		Log.Information("Alert polling stopped");
	}

	public Task RefreshAsync() => PollAsync(CancellationToken.None);

	public async Task HandlePushAsync(HandAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		Log.Information("Alert {AlertId} arrived by push ({Kind})", alert.Id, alert.Kind);

		await AdmitAsync(alert).ConfigureAwait(false);
	}

	public async Task AcknowledgeAsync(HandAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		// Remove it locally first. The responder has silenced the alarm and
		// the UI must reflect that immediately, whatever the network is
		// doing; the server-side acknowledgement below is what stops it
		// coming back on the next poll, and if that fails the poll will
		// simply re-admit it — which is the correct outcome, not a bug.
		await RemoveAsync(alert.Id).ConfigureAwait(false);

		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			return;
		}

		var result = await _reach.AcknowledgeAsync(token, alert.Id, CancellationToken.None).ConfigureAwait(false);
		if (!result.Success)
		{
			Log.Warning(
				"Alert {AlertId} could not be acknowledged: {Failure} {Message}",
				alert.Id, result.Failure, result.Message);

			await HandleFailureAsync(result.Failure).ConfigureAwait(false);
		}
	}

	public async Task ShowContactAsync(HandAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		if (!alert.HasContact || alert.IsContactShown || alert.IsLoadingContact)
		{
			return;
		}

		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			return;
		}

		await _dispatcher.InvokeAsync(() => alert.IsLoadingContact = true).ConfigureAwait(false);

		try
		{
			var result = await _reach
				.GetContactAsync(token, alert.Id, CancellationToken.None)
				.ConfigureAwait(false);

			if (result.Success)
			{
				await _dispatcher.InvokeAsync(
					() => alert.Contact = result.Value ?? string.Empty).ConfigureAwait(false);

				Log.Information("Contact details viewed for alert {AlertId}", alert.Id);
			}
			else
			{
				Log.Warning(
					"Contact for alert {AlertId} could not be fetched: {Failure} {Message}",
					alert.Id, result.Failure, result.Message);

				await HandleFailureAsync(result.Failure).ConfigureAwait(false);
			}
		}
		finally
		{
			await _dispatcher.InvokeAsync(() => alert.IsLoadingContact = false).ConfigureAwait(false);
		}
	}

	public async Task AcknowledgeAllAsync()
	{
		foreach (var alert in Active.ToArray())
		{
			await AcknowledgeAsync(alert).ConfigureAwait(false);
		}
	}

	private async Task PollAsync(CancellationToken cancellationToken)
	{
		var configuration = _configuration.GetReachConfiguration();
		if (!configuration.OnDuty)
		{
			return;
		}

		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			return;
		}

		var result = await _reach.GetPendingAlertsAsync(token, cancellationToken).ConfigureAwait(false);
		if (!result.Success)
		{
			await HandleFailureAsync(result.Failure).ConfigureAwait(false);
			return;
		}

		foreach (var alert in result.Value ?? [])
		{
			await AdmitAsync(alert).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// The single door an alert comes through, whichever route it took.
	/// </summary>
	private async Task AdmitAsync(HandAlert alert)
	{
		// The removal notice is an instruction, not an alert, so it turns
		// back here before it can reach the list, the notification tray or
		// the alarm. Checked ahead of the expiry test below on purpose: a
		// notice that arrived late is not a stale emergency that has
		// stopped mattering, it is a statement about this handset's
		// enrolment that either still holds or does not — and the check
		// against Reach, not the clock, is what settles which.
		if (alert.IsDeviceRemoval)
		{
			await HandleRemovalNoticeAsync().ConfigureAwait(false);
			return;
		}

		var now = DateTimeOffset.UtcNow;

		// A push can be delivered late, and a handset back from a long time
		// out of signal should not start shouting about something that
		// stopped mattering an hour ago.
		if (alert.IsExpired(now))
		{
			Log.Debug("Alert {AlertId} ignored: already expired", alert.Id);
			return;
		}

		await _gate.WaitAsync().ConfigureAwait(false);
		bool admitted;
		try
		{
			if (_handled.Contains(alert.Id) || Active.Any(a => a.Id == alert.Id))
			{
				admitted = false;
			}
			else
			{
				admitted = true;
				await _dispatcher.InvokeAsync(() => Active.Insert(0, alert)).ConfigureAwait(false);
			}
		}
		finally
		{
			_gate.Release();
		}

		if (!admitted)
		{
			return;
		}

		Log.Information(
			"Alert {AlertId} admitted: {Kind} {Reference} (urgent={Urgent})",
			alert.Id, alert.Kind, alert.Reference, alert.IsUrgent);

		// The OS notification first: it is what a responder sees if the app
		// is not on screen, and it must be up even if the audio fails.
		try
		{
			await _presenter.PresentAsync(alert).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Alert {AlertId} could not be presented", alert.Id);
		}

		await _alarm.StartAsync(alert).ConfigureAwait(false);
	}

	private async Task RemoveAsync(long alertId)
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		bool nothingLeft;
		try
		{
			Remember(alertId);

			await _dispatcher.InvokeAsync(() =>
			{
				var existing = Active.FirstOrDefault(a => a.Id == alertId);
				if (existing is not null)
				{
					Active.Remove(existing);
				}
			}).ConfigureAwait(false);

			nothingLeft = Active.Count == 0;
		}
		finally
		{
			_gate.Release();
		}

		await _presenter.DismissAsync(alertId).ConfigureAwait(false);

		// The alarm is one alarm for any number of outstanding alerts, so it
		// only stops when the last of them is answered.
		if (nothingLeft)
		{
			await _alarm.StopAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Note an id as dealt with, keeping the set bounded.
	///
	/// <para>Without a bound this grows for the lifetime of the process,
	/// which on a duty handset left running for weeks is a slow leak. Two
	/// hundred is far more than the number of alerts that can still be
	/// in flight server-side, which is all this set needs to cover — once
	/// the server has recorded the acknowledgement the poll stops
	/// returning the alert anyway.</para>
	/// </summary>
	private void Remember(long alertId)
	{
		if (!_handled.Add(alertId))
		{
			return;
		}

		_handledOrder.Enqueue(alertId);

		while (_handledOrder.Count > 200)
		{
			_handled.Remove(_handledOrder.Dequeue());
		}
	}

	/// <summary>
	/// Act on a removal notice: ask Reach whether it is still true, and
	/// sign out if it agrees.
	///
	/// <para><b>The notice is a prompt to check, never an instruction to
	/// obey.</b> A push registration token outlives the device row it was
	/// registered against, so a notice can be delivered late to a handset
	/// whose responder has already signed in again — and signing that one
	/// out would take a working handset off the rota on the strength of a
	/// message about a pairing that no longer exists. Reach deleted the
	/// row before sending, so the session check is decisive: if this
	/// handset is still enrolled, the notice is not about it.</para>
	///
	/// <para>A handset that cannot reach Reach stays signed in. That is
	/// the right way round — an unverifiable instruction to stop
	/// listening is exactly the one not to act on, and the next poll to
	/// get through will find the 401 anyway.</para>
	/// </summary>
	private async Task HandleRemovalNoticeAsync()
	{
		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			return;
		}

		Log.Information("Removal notice received — checking with Reach");

		var result = await _reach.GetSessionAsync(token, CancellationToken.None).ConfigureAwait(false);
		if (result.Success)
		{
			Log.Information("Removal notice ignored: this handset is still enrolled");
			return;
		}

		await HandleFailureAsync(
			result.Failure,
			"An administrator has taken this handset off the alert rota. "
			+ "Sign in again to enrol it afresh.").ConfigureAwait(false);
	}

	private async Task HandleFailureAsync(ReachFailure failure, string? reason = null)
	{
		if (failure is not (ReachFailure.Unauthenticated or ReachFailure.NotEligible))
		{
			// Network and server failures are ordinary; the next poll tries
			// again. Nothing to tell the responder about a handset that is
			// briefly out of signal.
			return;
		}

		Log.Warning("This handset is no longer authorised ({Failure}) — signing out", failure);

		await StopAsync().ConfigureAwait(false);

		// Nothing outstanding may survive the sign-out. The alerts page is
		// about to be replaced by sign-in, and a notification left in the
		// tray would open an app that can no longer fetch anything about
		// it.
		await ClearActiveAsync().ConfigureAwait(false);

		await _configuration.ClearDeviceTokenAsync().ConfigureAwait(false);

		AuthenticationLost?.Invoke(this, new AuthenticationLostEventArgs(reason ?? ReasonFor(failure)));
	}

	/// <summary>What to tell the responder, when the notice did not say.</summary>
	private static string ReasonFor(ReachFailure failure) => failure switch
	{
		ReachFailure.NotEligible =>
			"This handset has been signed out because you are no longer listed as a "
			+ "certified telephone responder. Speak to your intergroup if that is wrong.",
		_ =>
			"This handset is no longer signed in to Reach. Sign in again to put it "
			+ "back on the rota.",
	};

	/// <summary>Drop every outstanding alert, its notification and the alarm.</summary>
	private async Task ClearActiveAsync()
	{
		foreach (var alert in Active.ToArray())
		{
			await RemoveAsync(alert.Id).ConfigureAwait(false);
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_pollLoop?.Cancel();
		_pollLoop?.Dispose();
		_gate.Dispose();
	}
}
