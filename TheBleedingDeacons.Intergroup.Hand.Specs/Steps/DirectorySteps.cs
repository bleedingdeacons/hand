using Reqnroll;
using Shouldly;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Specs.Support;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Steps;

/// <summary>The recipient picker: members, their numbers, committees.</summary>
[Binding]
public sealed class DirectorySteps(World world)
{
	private HandCommittee? _committee;

	[Given(@"^a member ""(.+)"" of ""(.+)""$")]
	public void AMemberOf(string name, string group) =>
		world.Member = new HandMember { Id = 1, AnonymousName = name, HomeGroup = group, Reachable = true };

	[Given(@"^a member ""(.+)"" of nothing$")]
	public void AMemberOfNothing(string name) =>
		world.Member = new HandMember { Id = 1, AnonymousName = name, Reachable = true };

	[Given(@"^nobody has enrolled a handset for them$")]
	public void Unreachable() => world.Member.ShouldNotBeNull().Reachable = false;

	[Given(@"^a member with mobile ""(.+)"" and landline ""(.+)""$")]
	public void WithBothNumbers(string mobile, string landline) =>
		world.Member = new HandMember
		{
			Id = 1,
			AnonymousName = "Dave S",
			Reachable = true,
			MobileNumber = mobile,
			LandlineNumber = landline,
			IsContactShown = true,
		};

	[Given(@"^a member with mobile ""(.+)"" and no landline$")]
	public void WithAMobileOnly(string mobile) =>
		world.Member = new HandMember
		{
			Id = 1,
			AnonymousName = "Dave S",
			Reachable = true,
			MobileNumber = mobile,
			IsContactShown = true,
		};

	[Given(@"^a member with no numbers at all$")]
	public void WithNoNumbers() =>
		world.Member = new HandMember { Id = 1, AnonymousName = "Dave S", Reachable = true };

	[Given(@"^they asked to be rung on their ""(.+)""$")]
	public void PreferredContact(string preference) =>
		world.Member.ShouldNotBeNull().PreferredContact = preference;

	[When(@"^their numbers have been asked for$")]
	public void NumbersFetched() => world.Member.ShouldNotBeNull().IsContactShown = true;

	[Then(@"^the picker shows ""(.+)""$")]
	public void PickerShows(string line) => world.Member.ShouldNotBeNull().Display.ShouldBe(line);

	[Then(@"^the row reads ""(.*)""$")]
	public void RowReads(string line)
	{
		var member = world.Member.ShouldNotBeNull();

		// Whichever of the two second-line notes the scenario means: a row
		// says at most one of them, and asking for "the row" rather than
		// naming the property is how a feature file should be able to put
		// it.
		var note = member.ReachabilityNote.Length > 0 ? member.ReachabilityNote : member.NoNumberNote;

		note.ShouldBe(line);
	}

	[Then(@"^the first line reads ""(.+)""$")]
	public void FirstLine(string line) => world.Member.ShouldNotBeNull().FirstNumberLine.ShouldBe(line);

	[Then(@"^the second line reads ""(.+)""$")]
	public void SecondLine(string line) => world.Member.ShouldNotBeNull().SecondNumberLine.ShouldBe(line);

	/// <summary>
	/// Asked of the type rather than of an instance, because the claim is
	/// that there is nowhere to put an address — not that this particular
	/// row happens to have none.
	/// </summary>
	[Then(@"^a directory row has no field an email address could go in$")]
	public void NoAddress() =>
		typeof(HandMember).GetProperties()
			.Select(p => p.Name)
			.ShouldNotContain(name =>
				name.Contains("Email", StringComparison.OrdinalIgnoreCase)
				|| name.Contains("Address", StringComparison.OrdinalIgnoreCase));

	[Given(@"^a committee of (\d+) handsets$")]
	public void ACommittee(int handsets) =>
		_committee = new HandCommittee { Slug = "public-information", Name = "Public Information", Handsets = handsets };

	[Given(@"^a committee two deep$")]
	public void ANestedCommittee() =>
		_committee = new HandCommittee { Slug = "literature", Name = "Literature", Depth = 2, Handsets = 3 };

	[Then(@"^it reads ""(.+)""$")]
	public void CommitteeReads(string line) => _committee.ShouldNotBeNull().HandsetsLine.ShouldBe(line);

	[Then(@"^it (can|cannot) be sent to$")]
	public void CommitteeReachable(string verdict) =>
		_committee.ShouldNotBeNull().Reachable
			.ShouldBe(string.Equals(verdict, "can", StringComparison.Ordinal));

	[Then(@"^its name carries no dashes$")]
	public void NoDashes() => _committee.ShouldNotBeNull().Name.ShouldNotContain("—");

	[Then(@"^its row is indented by (\d+)$")]
	public void Indented(int indent) => _committee.ShouldNotBeNull().Indent.ShouldBe(indent);
}
