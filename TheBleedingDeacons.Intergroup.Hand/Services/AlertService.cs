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
/// </summary>
public sealed class AlertService : IAlertService, IDisposable
{
	private readonly IReachClient _reach;
	private readonly IConfigurationService _configuration;
	private readonly IAlertAlarm _alarm;
	private readonly IPlatformAlertPresenter _presenter;

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
		IPlatformAlertPresenter presenter)
	{
		_reach = reach ?? throw new ArgumentNullException(nameof(reach));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_alarm = alarm ?? throw new ArgumentNullException(nameof(alarm));
		_presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
	}

	public ObservableCollection<HandAlert> Active { get; } = [];

	public event EventHandler? AuthenticationLost;

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

		await MainThread.InvokeOnMainThreadAsync(() => alert.IsLoadingContact = true).ConfigureAwait(false);

		try
		{
			var result = await _reach
				.GetContactAsync(token, alert.Id, CancellationToken.None)
				.ConfigureAwait(false);

			if (result.Success)
			{
				await MainThread.InvokeOnMainThreadAsync(
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
			await MainThread.InvokeOnMainThreadAsync(() => alert.IsLoadingContact = false).ConfigureAwait(false);
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
				await MainThread.InvokeOnMainThreadAsync(() => Active.Insert(0, alert)).ConfigureAwait(false);
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

			await MainThread.InvokeOnMainThreadAsync(() =>
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

	private async Task HandleFailureAsync(ReachFailure failure)
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
		await _configuration.ClearDeviceTokenAsync().ConfigureAwait(false);

		AuthenticationLost?.Invoke(this, EventArgs.Empty);
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
