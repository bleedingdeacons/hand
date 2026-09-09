using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// How a picker row reads once a responder has asked for the numbers.
///
/// <para>All of this is ordering and labelling, which sounds like the
/// sort of thing a layout should do. It lives on the model because the
/// two decisions underneath it are not cosmetic: which number a member
/// asked to be rung on, and whether the question even arises. Getting
/// either wrong sends somebody to the wrong phone.</para>
/// </summary>
public sealed class HandMemberTests
{
	[Fact]
	public void ShowsNothingBeforeTheNumbersHaveBeenAskedFor()
	{
		var member = new HandMember { Id = 1, AnonymousName = "Jo B." };

		Assert.False(member.IsContactShown);
		Assert.False(member.HasAnyNumber);
		Assert.Equal(string.Empty, member.FirstNumberLine);
		Assert.Equal(string.Empty, member.SecondNumberLine);

		// Not "no number on file" either: nobody has asked yet, and a row
		// that says so before the question would be stating a fact the
		// handset does not have.
		Assert.Equal(string.Empty, member.NoNumberNote);
	}

	[Fact]
	public void PutsThePreferredNumberFirst()
	{
		var member = Reveal("07700 900123", "0117 496 0123", "Landline");

		Assert.Equal("Landline 0117 496 0123 (preferred)", member.FirstNumberLine);
		Assert.Equal("07700 900123", member.SecondNumberLine);
	}

	[Fact]
	public void PutsTheMobileFirstWhenThatIsTheOneAskedFor()
	{
		var member = Reveal("07700 900123", "0117 496 0123", "Mobile");

		Assert.Equal("07700 900123 (preferred)", member.FirstNumberLine);
		Assert.Equal("Landline 0117 496 0123", member.SecondNumberLine);
	}

	[Fact]
	public void DoesNotTagAPreferenceWhenThereIsOnlyOneNumber()
	{
		// One number has nothing to be preferred over, and tagging the
		// only number on the row is noise.
		var member = Reveal("07700 900123", string.Empty, "Mobile");

		Assert.Equal("07700 900123", member.FirstNumberLine);
		Assert.Equal(string.Empty, member.SecondNumberLine);
	}

	[Fact]
	public void IgnoresAPreferenceForALandlineThatIsNotThere()
	{
		// Reach keeps a member's last saved choice even for a landline
		// since deleted, so the preference is read together with the
		// numbers rather than trusted on its own. Leading with an empty
		// line would leave the row looking blank.
		var member = Reveal("07700 900123", string.Empty, "Landline");

		Assert.False(member.PrefersLandline);
		Assert.Equal("07700 900123", member.FirstNumberLine);
	}

	[Fact]
	public void ShowsALandlineOnlyMemberWithoutTaggingIt()
	{
		var member = Reveal(string.Empty, "0117 496 0123", "Mobile");

		Assert.True(member.HasAnyNumber);

		// The preference says Mobile and there is no mobile, so the
		// landline lands in the second slot. Only one label is showing
		// either way, so the slot it lands in does not matter.
		Assert.Equal(string.Empty, member.FirstNumberLine);
		Assert.Equal("Landline 0117 496 0123", member.SecondNumberLine);
	}

	[Fact]
	public void SaysSoWhenThereIsNothingToDial()
	{
		var member = Reveal(string.Empty, string.Empty, "Mobile");

		Assert.True(member.IsContactShown);
		Assert.False(member.HasAnyNumber);
		Assert.Equal("No number on file", member.NoNumberNote);
	}

	[Fact]
	public void RaisesTheRowsLinesWhenTheNumbersArrive()
	{
		// The row is already on screen when the answer lands, so the
		// computed lines have to announce themselves — a plain property
		// would leave the button sitting where the number should be.
		var member = new HandMember { Id = 1 };
		var changed = new List<string>();
		member.PropertyChanged += (_, e) => changed.Add(e.PropertyName ?? string.Empty);

		member.MobileNumber = "07700 900123";

		Assert.Contains(nameof(HandMember.FirstNumberLine), changed);
		Assert.Contains(nameof(HandMember.SecondNumberLine), changed);
		Assert.Contains(nameof(HandMember.HasAnyNumber), changed);
	}

	[Fact]
	public void CarriesNoAddress()
	{
		// The reason a directory on every handset is acceptable at all.
		// Numbers arriving does not change it: the picker still has
		// nowhere to put an email, and a recipient is still resolved to
		// one server-side.
		Assert.DoesNotContain(
			typeof(HandMember).GetProperties(),
			property => property.Name.Contains("Email", StringComparison.OrdinalIgnoreCase));
	}

	private static HandMember Reveal(string mobile, string landline, string preferred) =>
		new()
		{
			Id = 1,
			AnonymousName = "Jo B.",
			MobileNumber = mobile,
			LandlineNumber = landline,
			PreferredContact = preferred,
			IsContactShown = true,
		};
}
