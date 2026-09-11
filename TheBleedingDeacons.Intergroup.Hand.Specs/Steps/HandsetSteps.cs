using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>
/// The state a scenario puts the handset in before anything arrives, and
/// the questions it asks about the handset itself rather than about one
/// alert.
/// </summary>
[Binding]
public sealed class HandsetSteps(World world)
{
	[Given(@"^this handset is signed in to Reach$")]
	public void SignedIn() => world.Configuration.DeviceToken = "device-token";

	[Given(@"^this handset has no device token$")]
	public void NoToken() => world.Configuration.DeviceToken = string.Empty;

	[Given(@"^the handset is in a meeting$")]
	public void InAMeeting() => world.Configuration.Reach.InMeeting = true;

	[Given(@"^the handset is not in a meeting$")]
	public void NotInAMeeting() => world.Configuration.Reach.InMeeting = false;

	[Given(@"^polling is turned off$")]
	public void PollingOff() => world.Configuration.Reach.Poll = false;

	[Given(@"^the notification tray refuses everything$")]
	public void TrayRefuses() => world.Presenter.ThrowOnPresent = true;

	[Given(@"^Reach will refuse the acknowledgement$")]
	public void AcknowledgementRefused() =>
		world.Reach.Acknowledgement = ReachResult<bool>.Fail(ReachFailure.Server, "no");

	[Given(@"^Reach cannot be reached at all$")]
	public void ReachIsUnreachable()
	{
		world.Reach.PendingAlerts =
			ReachResult<IReadOnlyList<HandAlert>>.Fail(ReachFailure.Network, "no route to host");
		world.Reach.Session =
			ReachResult<DeviceSession>.Fail(ReachFailure.Network, "no route to host");
	}

	// Also a When: a scenario can revoke the token part-way through, after
	// an alert is already on screen, which is the case the sign-out has to
	// clear up after.
	[Given(@"^Reach no longer knows this handset$")]
	[When(@"^Reach no longer knows this handset$")]
	public void ReachHasForgottenIt()
	{
		world.Reach.Session =
			ReachResult<DeviceSession>.Fail(ReachFailure.Unauthenticated, "unknown device");
		world.Reach.PendingAlerts =
			ReachResult<IReadOnlyList<HandAlert>>.Fail(ReachFailure.Unauthenticated, "unknown device");
	}

	[Given(@"^this responder is no longer a certified telephone responder$")]
	public void NotEligible()
	{
		world.Reach.Session =
			ReachResult<DeviceSession>.Fail(ReachFailure.NotEligible, string.Empty);
		world.Reach.PendingAlerts =
			ReachResult<IReadOnlyList<HandAlert>>.Fail(ReachFailure.NotEligible, string.Empty);
	}

	[When(@"^the handset is silenced$")]
	public async Task Silence() => await world.Handset.SilenceAsync();

	[When(@"^the handset starts listening$")]
	public async Task Start() => await world.Handset.StartAsync();

	[When(@"^the handset stops listening$")]
	public async Task Stop() => await world.Handset.StopAsync();

	[Then(@"^the handset is still signed in$")]
	public void StillSignedIn()
	{
		world.SignedOutReason.ShouldBeNull();
		world.Configuration.DeviceToken.ShouldNotBeEmpty();
	}

	[Then(@"^the handset has signed itself out$")]
	public void SignedOut()
	{
		world.SignedOutReason.ShouldNotBeNull();
		world.Configuration.DeviceToken.ShouldBeEmpty();
	}

	[Then(@"^the responder is told something that mentions ""(.+)""$")]
	public void ToldWhy(string words) =>
		world.SignedOutReason.ShouldNotBeNull().ShouldContain(words);

	[Then(@"^Reach was asked whether this handset is still enrolled$")]
	public void SessionChecked() => world.Reach.SessionChecks.ShouldBeGreaterThan(0);

	[Then(@"^Reach was never asked for pending alerts$")]
	public void NeverPolled() => world.Reach.Polls.ShouldBe(0);

	[Then(@"^Reach was asked for pending alerts$")]
	public void Polled() => world.Reach.Polls.ShouldBeGreaterThan(0);
}
