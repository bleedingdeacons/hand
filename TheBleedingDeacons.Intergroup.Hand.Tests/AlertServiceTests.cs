using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The alert loop — the part of Hand that decides whether a handset
/// rings.
///
/// <para>Alerts arrive by two overlapping routes on purpose, so the
/// behaviour that matters most is what happens when the same alert
/// arrives twice, when one arrives that has already been answered, and
/// when one arrives that is not an alert at all. Each of those has a
/// wrong answer that is either a phone that will not stop or a phone
/// that never starts.</para>
/// </summary>
public sealed class AlertServiceTests
{
	private readonly FakeReachClient _reach = new();
	private readonly FakeConfigurationService _config = new() { DeviceToken = "abc123" };
	private readonly FakeAlarm _alarm = new();
	private readonly FakePresenter _presenter = new();
	private readonly InlineDispatcher _dispatcher = new();

	private AlertService Build() => new(_reach, _config, _alarm, _presenter, _dispatcher);

	[Fact]
	public void Constructor_RefusesItsDependenciesBeingNull()
	{
		Assert.Throws<ArgumentNullException>(() => new AlertService(null!, _config, _alarm, _presenter, _dispatcher));
		Assert.Throws<ArgumentNullException>(() => new AlertService(_reach, null!, _alarm, _presenter, _dispatcher));
		Assert.Throws<ArgumentNullException>(() => new AlertService(_reach, _config, null!, _presenter, _dispatcher));
		Assert.Throws<ArgumentNullException>(() => new AlertService(_reach, _config, _alarm, null!, _dispatcher));
		Assert.Throws<ArgumentNullException>(() => new AlertService(_reach, _config, _alarm, _presenter, null!));
	}

	// ── Admission ─────────────────────────────────────────────────────

	[Fact]
	public async Task APushedAlertIsListed_Presented_AndRings()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Single(service.Active);
		Assert.Equal(7, service.Active[0].Id);
		Assert.Single(_presenter.Presented);
		Assert.Single(_alarm.Started);
		Assert.True(_alarm.IsSounding);
	}

	[Fact]
	public async Task APolledAlertIsAdmittedTheSameWay()
	{
		_reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([Alerts.New(7)]);
		using var service = Build();

		await service.RefreshAsync();

		Assert.Single(service.Active);
		Assert.Single(_alarm.Started);
	}

	[Fact]
	public async Task HandlePushAsync_RejectsNull()
	{
		using var service = Build();

		await Assert.ThrowsAsync<ArgumentNullException>(() => service.HandlePushAsync(null!));
	}

	/// <summary>
	/// The same alert by both routes is one entry and one alarm. This is
	/// the case the whole funnel exists for.
	/// </summary>
	[Fact]
	public async Task TheSameAlertArrivingTwiceProducesOneEntryAndOneAlarm()
	{
		_reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([Alerts.New(7)]);
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));
		await service.RefreshAsync();

		Assert.Single(service.Active);
		Assert.Single(_alarm.Started);
		Assert.Single(_presenter.Presented);
	}

	/// <summary>
	/// A push can be delivered late, and a handset back from a long time
	/// out of signal should not start shouting about something that
	/// stopped mattering an hour ago.
	/// </summary>
	[Fact]
	public async Task AnExpiredAlertIsIgnoredEntirely()
	{
		using var service = Build();

		await service.HandlePushAsync(
			Alerts.New(7, expiresAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600));

		Assert.Empty(service.Active);
		Assert.Empty(_alarm.Started);
		Assert.Empty(_presenter.Presented);
	}

	[Fact]
	public async Task AnAlertWithNoExpiryIsAdmitted()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7, expiresAt: 0));

		Assert.Single(service.Active);
	}

	/// <summary>
	/// The notification is what a responder sees when the app is not on
	/// screen, but a handset that refused notification permission must
	/// still ring — losing the alert because the notification failed is
	/// the failure this design exists to prevent.
	/// </summary>
	[Fact]
	public async Task StillRingsWhenTheNotificationCannotBePosted()
	{
		_presenter.ThrowOnPresent = true;
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Single(service.Active);
		Assert.Single(_alarm.Started);
	}

	/// <summary>Every mutation of the bound collection goes via the dispatcher.</summary>
	[Fact]
	public async Task MarshalsCollectionChangesToTheUiThread()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.True(_dispatcher.Invocations > 0);
	}

	// ── Acknowledgement ───────────────────────────────────────────────

	[Fact]
	public async Task AcknowledgingRemovesTheAlert_DismissesIt_AndTellsReach()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		await service.AcknowledgeAsync(service.Active[0]);

		Assert.Empty(service.Active);
		Assert.Equal([7L], _presenter.Dismissed);
		Assert.Equal([7L], _reach.Acknowledged);
		Assert.Equal(1, _alarm.StopCount);
	}

	/// <summary>
	/// One alarm serves any number of outstanding alerts, so it only stops
	/// when the last of them is answered.
	/// </summary>
	[Fact]
	public async Task TheAlarmKeepsGoingUntilTheLastAlertIsAnswered()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));
		await service.HandlePushAsync(Alerts.New(8));

		await service.AcknowledgeAsync(service.Active.First(a => a.Id == 7));
		Assert.Equal(0, _alarm.StopCount);

		await service.AcknowledgeAsync(service.Active.First(a => a.Id == 8));
		Assert.Equal(1, _alarm.StopCount);
	}

	/// <summary>
	/// An alert acknowledged on one route must not be re-admitted by the
	/// other before the server has caught up.
	/// </summary>
	[Fact]
	public async Task AnAcknowledgedAlertIsNotReAdmittedByTheNextPoll()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));
		await service.AcknowledgeAsync(service.Active[0]);

		_reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([Alerts.New(7)]);
		await service.RefreshAsync();

		Assert.Empty(service.Active);
		Assert.Single(_alarm.Started);
	}

	[Fact]
	public async Task AcknowledgeAllAnswersEveryOutstandingAlert()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));
		await service.HandlePushAsync(Alerts.New(8));
		await service.HandlePushAsync(Alerts.New(9));

		await service.AcknowledgeAllAsync();

		Assert.Empty(service.Active);
		Assert.Equal([7L, 8L, 9L], _reach.Acknowledged.Order());
		Assert.Equal(1, _alarm.StopCount);
	}

	[Fact]
	public async Task AcknowledgeAsync_RejectsNull()
	{
		using var service = Build();

		await Assert.ThrowsAsync<ArgumentNullException>(() => service.AcknowledgeAsync(null!));
	}

	/// <summary>
	/// The responder has silenced the alarm; the UI must reflect that
	/// whatever the network is doing. A failed server-side acknowledgement
	/// means the next poll re-admits the alert, which is correct.
	/// </summary>
	[Fact]
	public async Task SilencesTheAlarmEvenWhenReachRefusesTheAcknowledgement()
	{
		_reach.Acknowledgement = ReachResult<bool>.Fail(ReachFailure.Network, "offline");
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		await service.AcknowledgeAsync(service.Active[0]);

		Assert.Empty(service.Active);
		Assert.Equal(1, _alarm.StopCount);
	}

	/// <summary>No token, nothing to tell the server with.</summary>
	[Fact]
	public async Task DoesNotCallReachWhenTheHandsetHasNoToken()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));
		_config.DeviceToken = string.Empty;

		await service.AcknowledgeAsync(service.Active[0]);

		Assert.Empty(_reach.Acknowledged);
	}

	// ── Contact details ───────────────────────────────────────────────

	[Fact]
	public async Task FetchesTheContactOnlyWhenAsked()
	{
		using var service = Build();
		var alert = Alerts.New(7);
		alert.HasContact = true;
		await service.HandlePushAsync(alert);

		Assert.Empty(_reach.ContactsRequested);

		await service.ShowContactAsync(alert);

		Assert.Equal([7L], _reach.ContactsRequested);
		Assert.Equal("07700 900000", alert.Contact);
		Assert.False(alert.IsLoadingContact);
	}

	[Fact]
	public async Task DoesNotFetchAContactThereIsNoneOf()
	{
		using var service = Build();
		var alert = Alerts.New(7);

		await service.ShowContactAsync(alert);

		Assert.Empty(_reach.ContactsRequested);
	}

	/// <summary>
	/// Reach writes an audit entry for every contact fetch, so a second
	/// tap on an alert already showing one must not make a second request.
	/// </summary>
	[Fact]
	public async Task DoesNotFetchAContactTwice()
	{
		using var service = Build();
		var alert = Alerts.New(7);
		alert.HasContact = true;

		await service.ShowContactAsync(alert);
		await service.ShowContactAsync(alert);

		Assert.Single(_reach.ContactsRequested);
	}

	[Fact]
	public async Task ClearsTheLoadingFlagWhenTheContactFetchFails()
	{
		_reach.Contact = ReachResult<string>.Fail(ReachFailure.Network, "offline");
		using var service = Build();
		var alert = Alerts.New(7);
		alert.HasContact = true;

		await service.ShowContactAsync(alert);

		Assert.False(alert.IsLoadingContact);
		Assert.Equal(string.Empty, alert.Contact);
	}

	[Fact]
	public async Task ShowContactAsync_RejectsNull()
	{
		using var service = Build();

		await Assert.ThrowsAsync<ArgumentNullException>(() => service.ShowContactAsync(null!));
	}

	// ── Polling ───────────────────────────────────────────────────────

	/// <summary>
	/// Off duty is the responder saying stop. The handset keeps its token;
	/// it just stops asking and stops making noise.
	/// </summary>
	[Fact]
	public async Task DoesNotPollWhenOffDuty()
	{
		_config.Reach = new ReachConfiguration { BaseUrl = "https://example.test/", OnDuty = false };
		using var service = Build();

		await service.RefreshAsync();

		Assert.Equal(0, _reach.Polls);
	}

	[Fact]
	public async Task DoesNotPollWithoutAToken()
	{
		_config.DeviceToken = string.Empty;
		using var service = Build();

		await service.RefreshAsync();

		Assert.Equal(0, _reach.Polls);
	}

	/// <summary>
	/// A handset briefly out of signal is ordinary. It must not be signed
	/// out, and the responder must not be told anything.
	/// </summary>
	[Fact]
	public async Task ANetworkFailureIsNotASignOut()
	{
		_reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Fail(ReachFailure.Network, "offline");
		using var service = Build();
		var signedOut = 0;
		service.AuthenticationLost += (_, _) => signedOut++;

		await service.RefreshAsync();

		Assert.Equal(0, signedOut);
		Assert.Equal(0, _config.ClearCount);
	}

	[Fact]
	public async Task StartAsyncPollsImmediatelyAndStopsCleanly()
	{
		_reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Ok([Alerts.New(7)]);
		using var service = Build();

		await service.StartAsync();
		// StartAsync is fire-and-forget by design; give the first poll a
		// moment to land rather than reaching into the loop.
		for (var i = 0; i < 100 && service.Active.Count == 0; i++)
		{
			await Task.Delay(20);
		}

		Assert.Single(service.Active);

		await service.StopAsync();
		Assert.Equal(1, _alarm.StopCount);
	}

	[Fact]
	public async Task StartAsyncIsIdempotent()
	{
		using var service = Build();

		await service.StartAsync();
		await service.StartAsync();
		await service.StopAsync();
	}

	[Fact]
	public async Task StopAsyncIsSafeWhenNothingIsRunning()
	{
		using var service = Build();

		await service.StopAsync();

		Assert.Equal(1, _alarm.StopCount);
	}

	// ── Losing authorisation ──────────────────────────────────────────

	/// <summary>
	/// A 401 or a lapsed certification is the handset being told it is no
	/// longer on the rota: everything outstanding goes, the token goes,
	/// and the page is told to show sign-in.
	/// </summary>
	[Theory]
	[InlineData(ReachFailure.Unauthenticated)]
	[InlineData(ReachFailure.NotEligible)]
	public async Task SignsOutWhenReachSaysThisHandsetIsNoLongerAuthorised(ReachFailure failure)
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		_reach.PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Fail(failure, string.Empty);
		AuthenticationLostEventArgs? raised = null;
		service.AuthenticationLost += (_, e) => raised = e;

		await service.RefreshAsync();

		Assert.NotNull(raised);
		Assert.NotEmpty(raised.Reason);
		Assert.Empty(service.Active);
		Assert.Equal([7L], _presenter.Dismissed);
		Assert.Equal(1, _config.ClearCount);
	}

	[Fact]
	public async Task ExplainsALapsedCertificationDifferentlyFromALostSession()
	{
		var reasons = new List<string>();

		foreach (var failure in new[] { ReachFailure.NotEligible, ReachFailure.Unauthenticated })
		{
			var reach = new FakeReachClient
			{
				PendingAlerts = ReachResult<IReadOnlyList<HandAlert>>.Fail(failure, string.Empty),
			};
			using var service = new AlertService(
				reach, new FakeConfigurationService { DeviceToken = "abc" }, new FakeAlarm(), new FakePresenter(), new InlineDispatcher());
			service.AuthenticationLost += (_, e) => reasons.Add(e.Reason);

			await service.RefreshAsync();
		}

		Assert.Equal(2, reasons.Count);
		Assert.Contains("certified telephone responder", reasons[0], StringComparison.Ordinal);
		Assert.NotEqual(reasons[0], reasons[1], StringComparer.Ordinal);
	}

	// ── The removal notice ────────────────────────────────────────────

	/// <summary>
	/// The removal notice is an instruction, not an alert. It must never
	/// reach the list, the tray or the alarm — waking someone at 3am to
	/// tell them they have been taken off the rota would be absurd.
	/// </summary>
	[Fact]
	public async Task ARemovalNoticeNeverRings()
	{
		_reach.Session = ReachResult<DeviceSession>.Fail(ReachFailure.Unauthenticated, string.Empty);
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7, kind: HandAlert.KindDeviceRemoved));

		Assert.Empty(service.Active);
		Assert.Empty(_alarm.Started);
		Assert.Empty(_presenter.Presented);
	}

	/// <summary>
	/// Reach deleted the device row before sending, so a session check that
	/// still succeeds means the notice is about a pairing that no longer
	/// exists — and signing this handset out would take a working one off
	/// the rota.
	/// </summary>
	[Fact]
	public async Task ARemovalNoticeIsIgnoredWhenTheHandsetIsStillEnrolled()
	{
		_reach.Session = ReachResult<DeviceSession>.Ok(new DeviceSession { Authorised = true });
		using var service = Build();
		var signedOut = 0;
		service.AuthenticationLost += (_, _) => signedOut++;

		await service.HandlePushAsync(Alerts.New(7, kind: HandAlert.KindDeviceRemoved));

		Assert.Equal(1, _reach.SessionChecks);
		Assert.Equal(0, signedOut);
		Assert.Equal(0, _config.ClearCount);
		Assert.Equal("abc123", _config.DeviceToken);
	}

	[Fact]
	public async Task ARemovalNoticeSignsOutWhenReachAgrees()
	{
		_reach.Session = ReachResult<DeviceSession>.Fail(ReachFailure.Unauthenticated, string.Empty);
		using var service = Build();
		AuthenticationLostEventArgs? raised = null;
		service.AuthenticationLost += (_, e) => raised = e;

		await service.HandlePushAsync(Alerts.New(7, kind: HandAlert.KindDeviceRemoved));

		Assert.NotNull(raised);
		Assert.Contains("taken this handset off the alert rota", raised.Reason, StringComparison.Ordinal);
		Assert.Equal(1, _config.ClearCount);
	}

	/// <summary>
	/// An unverifiable instruction to stop listening is exactly the one not
	/// to act on. The next poll that gets through will find the 401 anyway.
	/// </summary>
	[Fact]
	public async Task ARemovalNoticeIsNotActedOnWhileTheHandsetCannotReachTheServer()
	{
		_reach.Session = ReachResult<DeviceSession>.Fail(ReachFailure.Network, "offline");
		using var service = Build();
		var signedOut = 0;
		service.AuthenticationLost += (_, _) => signedOut++;

		await service.HandlePushAsync(Alerts.New(7, kind: HandAlert.KindDeviceRemoved));

		Assert.Equal(0, signedOut);
		Assert.Equal(0, _config.ClearCount);
	}

	[Fact]
	public async Task ARemovalNoticeIsIgnoredWhenTheHandsetIsNotSignedInAnyway()
	{
		_config.DeviceToken = string.Empty;
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7, kind: HandAlert.KindDeviceRemoved));

		Assert.Equal(0, _reach.SessionChecks);
	}

	/// <summary>
	/// Checked ahead of the expiry test on purpose: a notice that arrived
	/// late is not a stale emergency, it is a statement about enrolment
	/// that either still holds or does not.
	/// </summary>
	[Fact]
	public async Task AnExpiredRemovalNoticeIsStillChecked()
	{
		_reach.Session = ReachResult<DeviceSession>.Ok(new DeviceSession());
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(
			7,
			kind: HandAlert.KindDeviceRemoved,
			expiresAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600));

		Assert.Equal(1, _reach.SessionChecks);
	}

	// ── Lifetime ──────────────────────────────────────────────────────

	[Fact]
	public void DisposeIsIdempotent()
	{
		var service = Build();

		service.Dispose();
		service.Dispose();
	}
}
