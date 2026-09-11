using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>Where the handset talks to, and how often it asks.</summary>
[Binding]
public sealed class SettingsSteps(World world)
{
	[Given(@"^a server address of ""(.*)""$")]
	public void AnAddress(string address) => Settings().BaseUrl = address;

	[Given(@"^a poll interval of (\d+) seconds$")]
	public void AnInterval(int seconds) => Settings().PollSeconds = seconds;

	[Given(@"^a handset nobody has configured$")]
	public void Untouched() => world.Settings = new ReachConfiguration();

	[When(@"^the settings are normalised$")]
	public void Normalise() => world.Settings = world.Settings.ShouldNotBeNull().Normalised();

	[Then(@"^it (is|is not) somewhere Hand will talk to$")]
	public void Validity(string verdict) =>
		world.Settings.ShouldNotBeNull().IsValid()
			.ShouldBe(string.Equals(verdict, "is", StringComparison.Ordinal));

	[Then(@"^the server address becomes ""(.+)""$")]
	public void AddressBecomes(string address) =>
		world.Settings.ShouldNotBeNull().BaseUrl.ShouldBe(address);

	[Then(@"^the poll interval is (\d+) seconds$")]
	public void IntervalIs(int seconds) =>
		world.Settings.ShouldNotBeNull().PollSeconds.ShouldBe(seconds);

	[Then(@"^it is not in a meeting$")]
	public void NotInAMeeting() => world.Settings.ShouldNotBeNull().InMeeting.ShouldBeFalse();

	[Then(@"^it polls as well as listening$")]
	public void PollsToo() => world.Settings.ShouldNotBeNull().Poll.ShouldBeTrue();

	/// <summary>
	/// Whatever the scenario has built so far, so two Givens can describe
	/// one set of settings between them.
	/// </summary>
	private ReachConfiguration Settings() =>
		world.Settings ??= new ReachConfiguration { BaseUrl = "https://aa-bristol.org/" };
}
