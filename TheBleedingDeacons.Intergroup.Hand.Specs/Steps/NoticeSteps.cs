using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>
/// The acknowledgement notice: Reach reporting on another message rather
/// than raising one of its own.
///
/// <para>Scenarios name the message rather than the alert, because that
/// is what a notice can quote — the id it would otherwise carry belongs
/// to whichever copy the other responder happened to answer.</para>
/// </summary>
[Binding]
public sealed class NoticeSteps(World world)
{
	[When(@"^a red alert (\d+) arrives by push, as another copy of message (\d+)$")]
	public async Task AnotherCopyArrives(long id, long message) =>
		await world.Handset.HandlePushAsync(
			world.Alert(id, messageUuid: MessageUuid(message)));

	[When(@"^(.+) acknowledges message (\d+) elsewhere, and notice (\d+) arrives$")]
	public async Task NoticeArrives(string who, long message, long noticeId) =>
		await world.Handset.HandlePushAsync(world.Notice(noticeId, MessageUuid(message), who));

	[When(@"^an anonymous notice (\d+) about message (\d+) arrives$")]
	public async Task AnonymousNoticeArrives(long noticeId, long message) =>
		await world.Handset.HandlePushAsync(
			world.Notice(noticeId, MessageUuid(message), by: string.Empty));

	[When(@"^a notice (\d+) arrives naming no message$")]
	public async Task NoticeNamingNothing(long noticeId) =>
		await world.Handset.HandlePushAsync(world.Notice(noticeId, aboutMessageUuid: string.Empty));

	[When(@"^an expired notice (\d+) about message (\d+) arrives$")]
	public async Task ExpiredNoticeArrives(long noticeId, long message) =>
		await world.Handset.HandlePushAsync(world.Notice(
			noticeId,
			MessageUuid(message),
			expiresAt: DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds()));

	[Then(@"^notice (\d+) is on screen$")]
	public void NoticeOnScreen(long id) => world.OnScreen(id).ShouldNotBeNull();

	[Then(@"^notice (\d+) is no longer on screen$")]
	public void NoticeNotOnScreen(long id) => world.OnScreen(id).ShouldBeNull();

	[Then(@"^notice (\d+)'s button says ""(.+)""$")]
	public void NoticeButtonSays(long id, string label) =>
		world.OnScreen(id).ShouldNotBeNull().ActionLabel.ShouldBe(label);

	[Then(@"^notice (\d+) reports that ""(.+)"" answered$")]
	public void NoticeNames(long id, string who) =>
		world.OnScreen(id).ShouldNotBeNull().AcknowledgedByName.ShouldBe(who);

	/// <summary>
	/// The uuid <see cref="World.Alert"/> gives the alert of that number,
	/// so a feature file can talk about "message 1" and mean the send
	/// alert 1 was one delivery of.
	/// </summary>
	private static string MessageUuid(long message) => $"message-{message}";
}
