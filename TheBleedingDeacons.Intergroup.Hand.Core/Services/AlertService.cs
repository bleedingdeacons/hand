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
/// <para>Two kinds that arrive on this loop are not alerts at all. A
/// <see cref="HandAlert.KindDeviceRemoved"/> notice is Reach telling
/// this handset it has been taken off the rota, and it is intercepted at
/// the door and turned into a sign-out — see
/// <see cref="HandleRemovalNoticeAsync"/>. A
/// <see cref="HandAlert.KindMessageAcknowledged"/> notice says another
/// responder has picked something up: it is admitted like anything else
/// so it can be read, but it never reaches the alarm, and on the way in
/// it takes the message it reports on off this handset — see
/// <see cref="RemoveMessageAsync"/>.</para>
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

	/// <summary>
	/// Whether the alarm is currently sounding.
	///
	/// <para>Tracked so that it is asked to stop once rather than every
	/// time something leaves the outstanding set. Without this, clearing
	/// the last alert in two steps — settle the card, then close it —
	/// stopped the alarm twice, which is harmless on the current alarm and
	/// exactly the sort of thing that stops being harmless later.</para>
	///
	/// <para>Read and written under <see cref="_gate"/>, with the actual
	/// call made outside it: the alarm is platform code and holding a lock
	/// across it is how a deadlock gets written.</para>
	/// </summary>
	private bool _alarmSounding;

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

		var configuration = _configuration.GetReachConfiguration();

		// Polling turned off. Push still arrives and still alarms — this
		// stops the asking, not the alerting — and RefreshAsync still
		// works, because that is a responder deliberately pulling rather
		// than the app deciding to.
		if (!configuration.Poll)
		{
			Log.Information("Alert polling is turned off; this handset is relying on push alone");
			return;
		}

		var cts = new CancellationTokenSource();
		_pollLoop = cts;

		var interval = TimeSpan.FromSeconds(configuration.PollSeconds);

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

		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			_alarmSounding = false;
		}
		finally
		{
			_gate.Release();
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

		// Already settled: this is the second press, which is Close. The
		// card goes and nothing else happens — Reach was told the first
		// time round, and a notice is told below on its only press.
		if (alert.AcknowledgedHere)
		{
			await RemoveAsync(alert.Id).ConfigureAwait(false);
			return;
		}

		// A notice is news rather than a job, so its one button removes it
		// outright. Everything else is silenced and settled but *kept*:
		// the responder has just accepted a call and now needs the
		// reference and the Show contact button to make it. Removing the
		// card at that moment was the old behaviour and it was exactly
		// backwards.
		if (alert.IsAcknowledgementNotice)
		{
			await RemoveAsync(alert.Id).ConfigureAwait(false);
		}
		else
		{
			await SettleAsync(alert).ConfigureAwait(false);
		}

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
		// Acknowledged *and* closed, unlike a single press.
		//
		// <para>Acknowledging one alert now keeps its card, because the
		// responder has taken that job and needs its reference and contact
		// to do it. Nobody takes on five jobs at once by pressing one
		// button, so this is the other thing: clearing a screen. Leaving
		// five settled cards behind would make the button's name a
		// lie.</para>
		foreach (var alert in Active.ToArray())
		{
			await AcknowledgeAsync(alert).ConfigureAwait(false);

			if (alert.AcknowledgedHere)
			{
				await RemoveAsync(alert.Id).ConfigureAwait(false);
			}
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
	/// <summary>
	/// Whether this run has already told Reach about the fault.
	///
	/// <para>Once per run, not once per push. The fault is a property of
	/// the handset rather than of any one alert, the server records the
	/// same thing every time, and a handset that cannot read anything
	/// would otherwise report on every message it receives — most of it
	/// while nobody is watching. Resetting on relaunch is deliberate: it
	/// is the cheapest way to keep the timestamp fresh for an admin
	/// deciding whether this broke last night or last spring.</para>
	/// </summary>
	private bool _reportedUnreadable;

	/// <summary>
	/// Tell Reach the alerts cannot be read. Failures are swallowed: this
	/// is a diagnostic, and a handset that cannot reach the server has a
	/// larger problem which its own logging already covers.
	/// </summary>
	public async Task ReportUnreadableAsync()
	{
		if (_reportedUnreadable)
		{
			return;
		}

		_reportedUnreadable = true;

		try
		{
			var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
			if (token.Length == 0)
			{
				return;
			}

			await _reach.ReportUnreadableAsync(token, CancellationToken.None).ConfigureAwait(false);
			Log.Warning("Told Reach this handset cannot read its alerts");
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Could not tell Reach this handset cannot read its alerts");
		}
	}

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

		// A notice does two things, and this is the first: it marks the
		// alert it reports on as already answered, wherever that alert is
		// still sitting in this handset's list. Done before the expiry and
		// duplicate checks below on purpose — a notice is a statement
		// about the past that goes on being true, so a late or repeated
		// one should still apply what it says even where it is not itself
		// worth showing again.
		if (alert.IsAcknowledgementNotice)
		{
			await RemoveMessageAsync(alert).ConfigureAwait(false);
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

		// The second thing a notice does not do: alarm. Everything else
		// admitted here is something a responder is being asked to act on;
		// a notice is the app being told somebody already has, and there
		// is no hour of the night at which that is worth a siren.
		if (alert.IsQuiet)
		{
			return;
		}

		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			_alarmSounding = true;
		}
		finally
		{
			_gate.Release();
		}

		await _alarm.StartAsync(alert).ConfigureAwait(false);
	}

	/// <summary>
	/// Mark an alert as taken by this responder: silence it, drop its
	/// notification, and leave the card on screen.
	///
	/// <para>Everything <see cref="RemoveAsync"/> does except the removal.
	/// The id is remembered so neither route re-admits it, the tray
	/// notification goes because the alarm is over, and the alarm itself
	/// stops once nothing unsettled is left.</para>
	/// </summary>
	private async Task SettleAsync(HandAlert alert)
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		bool nothingLeft;
		try
		{
			Remember(alert.Id);

			await _dispatcher.InvokeAsync(() => alert.AcknowledgedHere = true).ConfigureAwait(false);

			nothingLeft = _alarmSounding && !Active.Any(a => !a.IsSettled);
			_alarmSounding &= !nothingLeft;
		}
		finally
		{
			_gate.Release();
		}

		await _presenter.DismissAsync(alert.Id).ConfigureAwait(false);

		if (nothingLeft)
		{
			await _alarm.StopAsync().ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Take a message off this handset because somebody else answered it.
	///
	/// <para><b>Removed, not marked.</b> An answered message is over: the
	/// responder who took it has the job, and leaving thirty other people
	/// a card to dismiss one by one is work invented for no reason. Reach
	/// stops serving the message at the same moment, which is what keeps
	/// the next poll from handing it straight back.</para>
	///
	/// <para><b>Matched on the message uuid, never on an id.</b> The id a
	/// notice could quote is the id of whichever copy the other responder
	/// happened to answer, and a message sent to a responder holding two
	/// handsets is two rows with two ids. The uuid is the only thing the
	/// copies share — see <see cref="HandAlert.MessageUuid"/>.</para>
	///
	/// <para>Finding nothing is normal and not a fault: the alert may have
	/// been acknowledged here already, or expired out of the list, or this
	/// handset may never have had it. The notice is still shown either
	/// way, because "Jo answered the 3am callback" is worth reading
	/// whether or not the original was ever on screen.</para>
	/// </summary>
	private async Task RemoveMessageAsync(HandAlert notice)
	{
		var messageUuid = notice.AcknowledgesMessage;
		if (messageUuid.Length == 0)
		{
			return;
		}

		// Snapshotted before removing: RemoveAsync mutates Active, and
		// every copy has to go rather than the first match — a responder
		// holding two handsets is the reason the uuid exists.
		var answered = Active
			.Where(a => string.Equals(a.MessageUuid, messageUuid, StringComparison.Ordinal))
			.Select(a => a.Id)
			.ToArray();

		foreach (var id in answered)
		{
			await RemoveAsync(id).ConfigureAwait(false);
		}

		Log.Information(
			"Message {MessageUuid} was answered by {Responder}; removed {Count} alert(s) here",
			messageUuid,
			notice.AcknowledgedByName,
			answered.Length);
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

			// Counted on what is still outstanding rather than on the list
			// being empty: an acknowledged card stays on screen now, and
			// it must not keep the alarm going. Gated on the alarm actually
			// sounding so that clearing the last card in two steps does not
			// stop it twice.
			nothingLeft = _alarmSounding && !Active.Any(a => !a.IsSettled);
			_alarmSounding &= !nothingLeft;
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
