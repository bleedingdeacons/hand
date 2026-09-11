using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>
/// One alert on its own, with no handset around it: what its level reads
/// as, what colour that makes it, and what a lock screen is offered in
/// its place.
/// </summary>
[Binding]
public sealed class AlertModelSteps(World world)
{
	[Given(@"^an alert at level ""(.+)""$")]
	public void AnAlertAtLevel(string level)
	{
		var alert = world.Alert(1, level);
		alert.Priority = "normal";

		world.Subject = alert;
	}

	[Given(@"^an alert with no level and priority ""(.+)""$")]
	public void AnAlertWithNoLevel(string priority)
	{
		var alert = world.Alert(1, string.Empty);
		alert.Priority = priority;

		world.Subject = alert;
	}

	[Then(@"^its level reads as (red|yellow|blue)$")]
	public void LevelReadsAs(string level) =>
		world.Subject.ShouldNotBeNull().LevelOrDerived.ShouldBe(World.Level(level));

	[Then(@"^its card is (#[0-9A-F]{6}) on white$")]
	public void CardColour(string colour)
	{
		var alert = world.Subject.ShouldNotBeNull();

		alert.LevelBackground.ShouldBe(colour);
		alert.LevelForeground.ShouldBe("#FFFFFF");
	}

	[Then(@"^a redacted lock screen would read ""(.+)"" / ""(.+)""$")]
	public void LockScreenReads(string title, string body)
	{
		var alert = world.Subject.ShouldNotBeNull();

		alert.LockScreenTitle.ShouldBe(title);
		HandAlert.LockScreenBody.ShouldBe(body);
	}

	[Then(@"^the redacted lock screen mentions neither its subject nor its reference$")]
	public void LockScreenGivesNothingAway()
	{
		var alert = world.Subject.ShouldNotBeNull();

		alert.LockScreenTitle.ShouldNotContain(alert.Title);
		alert.LockScreenTitle.ShouldNotContain(alert.Reference);
		HandAlert.LockScreenBody.ShouldNotContain(alert.Reference);
	}

	[Given(@"^the time is (\d{4}-\d{2}-\d{2} \d{2}:\d{2})$")]
	public void TheTimeIs(string instant) =>
		world.Clock.SetUtcNow(World.Instant(instant));

	[Given(@"^an alert whose window shuts at (\d{4}-\d{2}-\d{2} \d{2}:\d{2})$")]
	public void AnAlertExpiringAt(string instant)
	{
		var alert = world.Alert(1);
		alert.ExpiresAt = World.Instant(instant).ToUnixTimeSeconds();

		world.Subject = alert;
	}

	[Given(@"^an alert with no window$")]
	public void AnAlertWithNoWindow() => world.Subject = world.Alert(1, expiresAt: 0);

	[Then(@"^it has (expired|not expired)$")]
	public void Expiry(string verdict) =>
		world.Subject.ShouldNotBeNull().IsExpired(world.Clock.GetUtcNow())
			.ShouldBe(string.Equals(verdict, "expired", StringComparison.Ordinal));

	[Then(@"^it can be replied to$")]
	public void CanReply() => world.Subject.ShouldNotBeNull().CanReply.ShouldBeTrue();

	[Then(@"^it cannot be replied to$")]
	public void CannotReply() => world.Subject.ShouldNotBeNull().CanReply.ShouldBeFalse();
}
