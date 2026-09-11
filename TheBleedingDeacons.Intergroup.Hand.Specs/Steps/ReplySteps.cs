using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>Replying to an alert, and handing one back to the rota.</summary>
[Binding]
public sealed class ReplySteps(World world)
{
	[Given(@"^Reach will refuse the reply$")]
	public void ReplyRefused() =>
		world.Reach.ReplyResult = ReachResult<bool>.Fail(ReachFailure.Server, "no");

	[Given(@"^Reach will refuse the pass-back$")]
	public void ResendRefused() =>
		world.Reach.ResendResult = ReachResult<bool>.Fail(ReachFailure.Server, "no");

	[Given(@"^a notice about somebody else's acknowledgement$")]
	public void ANotice() => world.Subject = world.Notice(9, "message-1");

	[Given(@"^an alert of kind ""(.+)""$")]
	public void AnAlertOfKind(string kind) =>
		world.Subject = world.Alert(1, kind: kind);

	[When(@"^I reply ""(.*)"" to alert (\d+)$")]
	public async Task Reply(string body, long id) =>
		world.LastCallSucceeded = await world.Handset.ReplyAsync(id, body);

	[When(@"^I pass alert (\d+) back to the rota$")]
	public async Task PassBack(long id)
	{
		var alert = world.OnScreen(id) ?? world.Built[id];

		world.LastCallSucceeded = await world.Handset.ResendAsync(alert);
	}

	[Then(@"^Reach was sent ""(.+)"" about alert (\d+)$")]
	public void ReplySent(string body, long id) =>
		world.Reach.Replies.ShouldContain((id, body));

	[Then(@"^Reach was sent no reply$")]
	public void NoReplySent() => world.Reach.Replies.ShouldBeEmpty();

	[Then(@"^the reply was (accepted|refused)$")]
	public void ReplyOutcome(string outcome) =>
		world.LastCallSucceeded.ShouldBe(string.Equals(outcome, "accepted", StringComparison.Ordinal));

	[Then(@"^Reach was asked to pass alert (\d+) back$")]
	public void PassBackSent(long id) => world.Reach.Resent.ShouldContain(id);
}
