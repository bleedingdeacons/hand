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

	private AlertService Build() => new(_reach, _config, _alarm, _presenter, _dispatcher);

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

	// ── It lands on the right alert ───────────────────────────────────

	[Fact]
	public async Task ANoticeMarksTheAlertItReportsOnAsAnswered()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(9, Message, by: "Jo B"));

		var original = service.Active.Single(a => a.Id == 7);
		Assert.True(original.IsAnswered);
		Assert.Equal("Jo B", original.AcknowledgedBy);
		Assert.Equal("Acknowledged by Jo B", original.AnsweredLine);
	}

	/// <summary>
	/// Matched on the uuid rather than the id, which is the whole reason
	/// the uuid exists: one message to a responder holding a phone and a
	/// tablet is two rows, and the notice names neither of them.
	/// </summary>
	[Fact]
	public async Task EveryCopyOfTheMessageIsMarked()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));
		await service.HandlePushAsync(Alerts.New(8, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.All(
			service.Active.Where(a => a.Id is 7 or 8),
			alert => Assert.True(alert.IsAnswered));
	}

	[Fact]
	public async Task AnUnrelatedMessageIsLeftAlone()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));
		await service.HandlePushAsync(Alerts.New(8, messageUuid: "a-different-message"));

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.False(service.Active.Single(a => a.Id == 8).IsAnswered);
	}

	/// <summary>
	/// An alert raised before Reach had the column carries the empty
	/// uuid. Every such alert would otherwise match every other one.
	/// </summary>
	[Fact]
	public async Task TheEmptyUuidMatchesNothing()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7));

		await service.HandlePushAsync(Alerts.Notice(9, aboutMessageUuid: string.Empty));

		Assert.False(service.Active.Single(a => a.Id == 7).IsAnswered);
	}

	/// <summary>
	/// Applied before the expiry check, like the removal notice: a notice
	/// is a statement about the past that goes on being true, not a stale
	/// emergency that has stopped mattering.
	/// </summary>
	[Fact]
	public async Task AnExpiredNoticeStillMarksTheAlertAnswered()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(
			9,
			Message,
			expiresAt: DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 3600));

		Assert.True(service.Active.Single(a => a.Id == 7).IsAnswered);

		// …and is not itself shown. It has nothing left to say that the
		// line above does not already say on the alert it reports on.
		Assert.DoesNotContain(service.Active, a => a.Id == 9);
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

	[Fact]
	public async Task AnAnsweredAlertOffersCloseInstead()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		var original = service.Active.Single(a => a.Id == 7);
		Assert.Equal("Acknowledge", original.ActionLabel);

		await service.HandlePushAsync(Alerts.Notice(9, Message));

		Assert.Equal("Close", original.ActionLabel);
	}

	/// <summary>
	/// The card is bound to these, so the change has to be announced or
	/// the button keeps its old text until something else redraws it.
	/// </summary>
	[Fact]
	public void MarkingAnAlertAnsweredRaisesTheBoundProperties()
	{
		var alert = Alerts.New(7, messageUuid: Message);
		var changed = new List<string?>();
		alert.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		alert.AcknowledgedBy = "Jo B";

		Assert.Contains(nameof(HandAlert.IsAnswered), changed);
		Assert.Contains(nameof(HandAlert.AnsweredLine), changed);
		Assert.Contains(nameof(HandAlert.ActionLabel), changed);
	}

	[Fact]
	public void AnUnansweredAlertHasNoAnsweredLine()
	{
		var alert = Alerts.New(7);

		Assert.False(alert.IsAnswered);
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
	public async Task ANoticeThatNamesNobodyStillNamesSomebody()
	{
		using var service = Build();
		await service.HandlePushAsync(Alerts.New(7, messageUuid: Message));

		await service.HandlePushAsync(Alerts.Notice(9, Message, by: string.Empty));

		Assert.Equal(
			HandAlert.UnknownResponder,
			service.Active.Single(a => a.Id == 7).AcknowledgedBy);
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
