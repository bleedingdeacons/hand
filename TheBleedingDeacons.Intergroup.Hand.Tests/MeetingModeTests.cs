using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// Meeting mode: everything happens, quietly.
///
/// <para>It replaced an on/off duty switch, and the tests worth having
/// are the ones that pin the difference. Off duty stopped the poll, so a
/// handset left the rota and nobody was told. This changes the volume and
/// nothing else — so what has to be asserted is not "it is silent" but
/// "it is silent <i>and everything else still happened</i>".</para>
/// </summary>
public sealed class MeetingModeTests
{
	private readonly FakeReachClient _reach = new();
	private readonly FakeConfigurationService _config = new() { DeviceToken = "abc123" };
	private readonly FakeAlarm _alarm = new();
	private readonly FakePresenter _presenter = new();
	private readonly InlineDispatcher _dispatcher = new();
	private readonly InMemoryAlertHistoryStore _historyStore = new();

	private AlertHistory History => field ??= new AlertHistory(_historyStore, _dispatcher);

	private AlertService Build() => new(_reach, _config, _alarm, _presenter, _dispatcher, History);

	private void InAMeeting() =>
		_config.Reach = new ReachConfiguration { BaseUrl = "https://example.test/", InMeeting = true };

	// ── The alarm ─────────────────────────────────────────────────────

	[Fact]
	public async Task ARedAlertStillRaisesTheAlarm_Silently()
	{
		InAMeeting();
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		// The alarm still runs — the responder still feels it, and the
		// state machine that stops it later still has something to stop.
		Assert.Single(_alarm.Started);
		Assert.True(_alarm.IsSounding);

		// It just does not make a noise.
		Assert.Equal([true], _alarm.StartedSilently);
	}

	[Fact]
	public async Task OutOfAMeetingTheAlarmIsAudible()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Equal([false], _alarm.StartedSilently);
	}

	// ── The notification ──────────────────────────────────────────────

	[Fact]
	public async Task TheNotificationIsStillPosted_Silently()
	{
		InAMeeting();
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Single(_presenter.Presented);
		Assert.Equal([true], _presenter.PresentedSilently);
	}

	/// <summary>
	/// One alert must not be silent in the tray and audible in the room.
	/// Both halves read the setting once, together.
	/// </summary>
	[Fact]
	public async Task TheAlarmAndTheNotificationAgree()
	{
		InAMeeting();
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Equal(_alarm.StartedSilently, _presenter.PresentedSilently);
	}

	// ── Everything else is unchanged ──────────────────────────────────

	[Fact]
	public async Task TheAlertIsStillListedAndStillOutstanding()
	{
		InAMeeting();
		using var service = Build();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Single(service.Active);
		Assert.False(service.Active[0].AcknowledgedHere);
	}

	[Fact]
	public async Task TheAlertIsStillRecordedInHistory()
	{
		InAMeeting();
		using var service = Build();
		await History.LoadAsync();

		await service.HandlePushAsync(Alerts.New(7));

		Assert.Contains(History.Entries, e => e.Id == 7);
	}

	[Fact]
	public async Task AcknowledgingStillWorksAndStillTellsReach()
	{
		InAMeeting();
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		await service.AcknowledgeAsync(service.Active[0]);

		Assert.Equal([7], _reach.Acknowledged);
	}

	// ── Switching it on mid-alarm ─────────────────────────────────────

	/// <summary>
	/// A responder reaching for the switch in a room full of people wants
	/// the noise to stop now, not at the next alert — and must not lose
	/// the alert in the process.
	/// </summary>
	[Fact]
	public async Task SilencingStopsTheNoiseAndKeepsTheAlert()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));
		Assert.True(_alarm.IsSounding);

		await service.SilenceAsync();

		Assert.False(_alarm.IsSounding);
		Assert.Equal(1, _alarm.StopCount);

		// Still outstanding, still listed, still nobody's job yet.
		Assert.Single(service.Active);
		Assert.False(service.Active[0].AcknowledgedHere);
	}

	/// <summary>
	/// Silencing is not signing off. The poll keeps running — which is the
	/// one thing the old duty switch got wrong.
	/// </summary>
	[Fact]
	public async Task SilencingLeavesThePollRunning()
	{
		using var service = Build();

		await service.SilenceAsync();
		await service.RefreshAsync();

		Assert.Equal(1, _reach.Polls);
	}

	[Fact]
	public async Task SilencingWhenNothingIsSoundingIsHarmless()
	{
		using var service = Build();

		await service.SilenceAsync();

		Assert.False(_alarm.IsSounding);
		Assert.Empty(service.Active);
	}
}
