using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The second message: what a handset does when Reach says another
/// responder has already picked something up.
///
/// <para>Two things have to hold, and each has a wrong answer that is
/// worse than the feature is good. A notice must never alarm — waking a
/// second responder at three in the morning to tell them the first one
/// answered is worse than saying nothing at all. And it must land on the
/// alert it reports on, which means matching by message uuid: the id it
/// could quote belongs to whichever copy the other responder happened
/// to answer, and a message sent to somebody holding two handsets is two
/// rows with two ids.</para>
/// </summary>
public sealed class AcknowledgementNoticeTests
{
	private const string Message = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b";

	private readonly FakeReachClient _reach = new();
	private readonly FakeConfigurationService _config = new() { DeviceToken = "abc123" };
	private readonly FakeAlarm _alarm = new();
	private readonly FakePresenter _presenter = new();
	private readonly InlineDispatcher _dispatcher = new();
	private readonly InMemoryAlertHistoryStore _historyStore = new();

	private AlertHistory History => field ??= new AlertHistory(_historyStore, _dispatcher);

	private AlertService Build() => new(_reach, _config, _alarm, _presenter, _dispatcher, History);

	// ── It never alarms ───────────────────────────────────────────────

	[Fact]
	public async Task ANoticeDoesNotStartTheAlarm()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.Empty(_alarm.Started);
	}

	/// <summary>
	/// Quiet is not the same as hidden. It still reaches the list and the
	/// notification tray, because "Jo answered the 3am callback" is worth
	/// reading — just not worth being woken by.
	/// </summary>
	[Fact]
	public async Task ANoticeIsStillShown()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.Single(service.Active);
		Assert.Single(_presenter.Presented);
	}

	/// <summary>
	/// An alert arriving alongside a notice still rings. The quiet path is
	/// chosen per alert, not switched on for the handset.
	/// </summary>
	[Fact]
	public async Task AnOrdinaryAlertStillAlarmsAfterANoticeHasArrived()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.Notice(9, Message));
		await service.HandlePushAsync(Alerts.New(10));

		Assert.Single(_alarm.Started);
		Assert.Equal(10L, _alarm.Started[0].Id);
	}

	// ── It clears the message from this handset ───────────────────────

	[Fact]
	public async Task ANoticeRemovesTheAlertItReportsOn()
	{
		// An answered message is over. The responder who took it has the
		// job; leaving everybody else a card to dismiss one by one is work
		// invented for no reason.
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(9, Message, by: "Jo B"));

		Assert.DoesNotContain(service.Active, a => a.Id == 7);
	}

	/// <summary>
	/// The notice stays. It is the whole of what this handset still needs
	/// to know, and the only thing left on screen about that message.
	/// </summary>
	[Fact]
	public async Task TheNoticeItselfRemains()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(9, Message, by: "Jo B"));

		var notice = Assert.Single(service.Active);
		Assert.Equal(9L, notice.Id);
		Assert.Equal("Jo B acknowledged", notice.Title);
	}

	/// <summary>
	/// Matched on the uuid rather than the id, which is the whole reason
	/// the uuid exists: one message to a responder holding a phone and a
	/// tablet is two rows, and the notice names neither of them.
	/// </summary>
	[Fact]
	public async Task EveryCopyOfTheMessageGoes()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));
		await service.HandlePushAsync(Alerts.New(8, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.DoesNotContain(service.Active, a => a.Id is 7 or 8);
	}

	[Fact]
	public async Task AnUnrelatedMessageIsLeftAlone()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));
		await service.HandlePushAsync(Alerts.New(8, messageUuid: "a-different-message"));

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.Contains(service.Active, a => a.Id == 8);
	}

	/// <summary>
	/// An alert raised before Reach had the column carries the empty uuid.
	/// Every such alert would otherwise match every other one, and one
	/// answered message would clear the lot.
	/// </summary>
	[Fact]
	public async Task TheEmptyUuidMatchesNothing()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		await service.HandlePushAsync(Alerts.Notice(9, aboutMessageUuid: string.Empty));

		Assert.Contains(service.Active, a => a.Id == 7);
	}

	/// <summary>
	/// Applied before the expiry check, like the removal notice: a notice
	/// is a statement about the past that goes on being true, not a stale
	/// emergency that has stopped mattering.
	/// </summary>
	[Fact]
	public async Task AnExpiredNoticeStillClearsTheMessage()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(
			9,
			Message,
			expiresAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600));

		Assert.DoesNotContain(service.Active, a => a.Id == 7);

		// …and is not itself shown. It has nothing left to say once the
		// message it reports on is gone.
		Assert.Empty(service.Active);
	}

	/// <summary>
	/// Clearing the last outstanding alert stops the alarm, whoever it was
	/// that answered. A handset left ringing about a job somebody else has
	/// taken is the failure this whole feature exists to remove.
	/// </summary>
	[Fact]
	public async Task ClearingTheLastAlertStopsTheAlarm()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));
		Assert.Equal(0, _alarm.StopCount);

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.Equal(1, _alarm.StopCount);
	}

	/// <summary>
	/// A notice about something this handset never had, or has already
	/// dealt with, is not a fault. It is shown and nothing else happens.
	/// </summary>
	[Fact]
	public async Task ANoticeAboutAMessageThisHandsetDoesNotHaveIsHarmless()
	{
		using var service = Build();

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.Single(service.Active);
		Assert.Empty(_alarm.Started);
	}

	// ── The button ────────────────────────────────────────────────────

	[Fact]
	public void AnOrdinaryAlertOffersAcknowledge()
	{
		Assert.Equal("Acknowledge", Alerts.New(7).ActionLabel);
	}

	[Fact]
	public void ANoticeOffersClose()
	{
		// There is nothing to acknowledge: the notice is the app being
		// told somebody else already did.
		Assert.Equal("Close", Alerts.Notice(9, Message).ActionLabel);
	}

	/// <summary>
	/// The general case the notice turned out to be one of. A message
	/// nobody has to take on offers Close whatever its kind, and whatever
	/// its level — a red fire drill everybody must see is still not a job.
	/// </summary>
	[Theory]
	[InlineData(HandAlert.LevelRed)]
	[InlineData(HandAlert.LevelYellow)]
	[InlineData(HandAlert.LevelBlue)]
	public void AnInformationalAlertOffersClose(string level)
	{
		var alert = Alerts.New(7, level: level, response: HandAlert.ResponseNone);

		Assert.Equal("Close", alert.ActionLabel);
		Assert.True(alert.IsSettled);
	}

	/// <summary>
	/// And the mirror: a job is a job at any level, including a blue one
	/// somebody still has to pick up.
	/// </summary>
	[Theory]
	[InlineData(HandAlert.LevelRed)]
	[InlineData(HandAlert.LevelYellow)]
	[InlineData(HandAlert.LevelBlue)]
	public void AFirstToRespondAlertOffersAcknowledge(string level)
	{
		var alert = Alerts.New(7, level: level, response: HandAlert.ResponseFirst);

		Assert.Equal("Acknowledge", alert.ActionLabel);
		Assert.False(alert.IsSettled);
	}

	/// <summary>
	/// Closing an informational card removes it outright rather than
	/// settling it in place. There is no reference to keep to hand and no
	/// call to make — the card was never a job.
	///
	/// <para>It still tells Reach. That is how the server learns this
	/// handset has dealt with its own copy, and what stops the next poll
	/// handing it straight back.</para>
	/// </summary>
	[Fact]
	public async Task ClosingAnInformationalAlertRemovesItAndStillTellsReach()
	{
		using var service = Build();
		await service.HandlePushAsync(
			Alerts.New(7, response: HandAlert.ResponseNone, messageUuid: Message));

		var card = service.Active.Single(a => a.Id == 7);
		await service.AcknowledgeAsync(card);

		Assert.Empty(service.Active);
		Assert.Contains(7, _reach.Acknowledged);
	}

	/// <summary>
	/// The card this responder took on keeps its place and says so, and
	/// its button becomes the one that clears it.
	/// </summary>
	[Fact]
	public async Task AnAcknowledgedCardSaysSoAndOffersClose()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		var card = service.Active.Single(a => a.Id == 7);
		Assert.Equal("Acknowledge", card.ActionLabel);
		Assert.Equal(string.Empty, card.AnsweredLine);

		await service.AcknowledgeAsync(card);

		Assert.Equal("Close", card.ActionLabel);
		Assert.Equal("Acknowledged by you", card.AnsweredLine);
	}

	/// <summary>
	/// The card is bound to these, so the change has to be announced or
	/// the button keeps its old text until something else redraws it.
	/// </summary>
	[Fact]
	public void SettlingAnAlertRaisesTheBoundProperties()
	{
		var alert = Alerts.New(7, messageUuid: Message);
		var changed = new List<string?>();
		alert.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		alert.AcknowledgedHere = true;

		Assert.Contains(nameof(HandAlert.IsSettled), changed);
		Assert.Contains(nameof(HandAlert.AnsweredLine), changed);
		Assert.Contains(nameof(HandAlert.ActionLabel), changed);
	}

	[Fact]
	public void AnUnacknowledgedAlertHasNoAnsweredLine()
	{
		var alert = Alerts.New(7);

		Assert.False(alert.IsSettled);
		Assert.Equal(string.Empty, alert.AnsweredLine);
	}

	// ── Naming ────────────────────────────────────────────────────────

	/// <summary>
	/// Reach sends its own generic stand-in where no name resolves, so
	/// this covers a notice that lost the property rather than one Reach
	/// meant to send nameless. Either way a card reading "Acknowledged
	/// by" and then nothing would look like a fault.
	/// </summary>
	[Fact]
	public void ANoticeThatNamesNobodyStillNamesSomebody()
	{
		var notice = Alerts.Notice(9, Message, by: string.Empty);

		Assert.Equal(HandAlert.UnknownResponder, notice.AcknowledgedByName);
	}

	/// <summary>
	/// Only a notice reports on another message. An ordinary alert that
	/// happened to carry a property of the same name is not one, and must
	/// not be able to mark somebody else's alert answered.
	/// </summary>
	[Fact]
	public void AnOrdinaryAlertReportsOnNothing()
	{
		var alert = Alerts.New(7);
		alert.Payload[HandAlert.PayloadAckMessageUuid] = Message;

		Assert.False(alert.IsAcknowledgementNotice);
		Assert.Equal(string.Empty, alert.AcknowledgesMessage);
	}
}
