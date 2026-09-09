using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// Somebody a message can be addressed to, as the picker shows them.
///
/// <para><b>A name, a home group, and an id. No address.</b> Reach's
/// directory deliberately carries no email, and a recipient is chosen by
/// <see cref="Id"/> and resolved to an address on the server — so one
/// responder never learns another's contact details in order to message
/// them. That is what makes a directory sitting on every handset
/// acceptable at all, and it is the reason this class has no field to
/// put an address in.</para>
///
/// <para>The home group is here because a rota has more than one Dave.
/// The anonymous name alone is frequently ambiguous, and the group is
/// what an intergroup actually uses to tell two of them apart.</para>
///
/// <para><b>The phone numbers are the one exception, and they are not
/// here when the row arrives.</b> They come from a second request a
/// responder has to ask for — see
/// <see cref="Services.Interfaces.IReachClient.GetMemberContactAsync"/>
/// — because Reach writes an audit row for each one and counts it
/// against an hourly cap. Fetching them with the list would spend both
/// on every name a responder happened to scroll past. Observable for
/// the same reason <see cref="HandAlert"/> is: the row is already on
/// screen when they arrive.</para>
/// </summary>
public sealed partial class HandMember : ObservableObject
{
	[JsonPropertyName("id")]
	public long Id { get; set; }

	/// <summary>
	/// The name this suite shows people. Never a real name — Unity has no
	/// field for one — and never an address.
	/// </summary>
	[JsonPropertyName("anonymous_name")]
	public string AnonymousName { get; set; } = string.Empty;

	/// <summary>Their home group, or empty when Unity holds none.</summary>
	[JsonPropertyName("home_group")]
	public string HomeGroup { get; set; } = string.Empty;

	/// <summary>
	/// Whether a message would actually arrive: whether Reach can find a
	/// live handset behind this member.
	///
	/// <para>Unreachable members are listed rather than hidden. Somebody
	/// looking for a name and not finding it has no way to tell whether
	/// they have mistyped it or the person simply has no phone enrolled,
	/// and the second is the answer they need.</para>
	/// </summary>
	[JsonPropertyName("reachable")]
	public bool Reachable { get; set; }

	/// <summary>
	/// The one line the picker shows: the name, then the home group where
	/// there is one.
	/// </summary>
	[JsonIgnore]
	public string Display => HomeGroup.Length > 0
		? $"{AnonymousName} — {HomeGroup}"
		: AnonymousName;

	/// <summary>
	/// The reason an unreachable member cannot be picked, for the row's
	/// second line. Empty when they can.
	/// </summary>
	[JsonIgnore]
	public string ReachabilityNote => Reachable ? string.Empty : "No handset enrolled";

	/// <summary>
	/// Their mobile number, once a responder has asked for it. Empty
	/// until then, and empty afterwards if Unity holds none.
	/// </summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial string MobileNumber { get; set; } = string.Empty;

	/// <summary>Their landline, on the same terms as the mobile.</summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial string LandlineNumber { get; set; } = string.Empty;

	/// <summary>
	/// Which number this member asked to be rung on, as Reach spells it
	/// — "Mobile" or "Landline".
	///
	/// <para>Only meaningful when both numbers are on file. Reach keeps
	/// the last saved choice even for a landline that has since been
	/// deleted, so this is read together with the numbers rather than
	/// trusted on its own — see <see cref="PrefersLandline"/>.</para>
	/// </summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial string PreferredContact { get; set; } = string.Empty;

	/// <summary>Whether the fetch is in flight, so the UI can say so.</summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial bool IsLoadingContact { get; set; }

	/// <summary>
	/// Whether the numbers have been asked for and answered.
	///
	/// <para>Set on a successful fetch whatever came back, so a member
	/// with nothing on file reads as "no number on file" rather than
	/// offering the button again — pressing it a second time would spend
	/// another of the responder's hourly allowance to be told the same
	/// thing.</para>
	/// </summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial bool IsContactShown { get; set; }

	/// <summary>Whether asking produced a number of either kind.</summary>
	[JsonIgnore]
	public bool HasAnyNumber => MobileNumber.Length > 0 || LandlineNumber.Length > 0;

	/// <summary>
	/// Whether the landline is the number to lead with: the member asked
	/// for it <em>and</em> there is one to ring. The second half is not
	/// redundant — see <see cref="PreferredContact"/>.
	/// </summary>
	[JsonIgnore]
	public bool PrefersLandline =>
		string.Equals(PreferredContact, "Landline", StringComparison.Ordinal)
		&& LandlineNumber.Length > 0;

	/// <summary>
	/// The mobile as the row shows it, tagged when it is the one the
	/// member asked to be rung on. Empty when there is no mobile.
	///
	/// <para>The tag appears only when both numbers are present: one
	/// number has nothing to be preferred over, and tagging the only
	/// number on the row is noise.</para>
	/// </summary>
	[JsonIgnore]
	public string MobileLine => MobileNumber.Length == 0
		? string.Empty
		: MobileNumber + (BothOnFile && !PrefersLandline ? " (preferred)" : string.Empty);

	/// <summary>
	/// The landline as the row shows it, named as one so it does not read
	/// as a second mobile, and tagged on the same terms.
	/// </summary>
	[JsonIgnore]
	public string LandlineLine => LandlineNumber.Length == 0
		? string.Empty
		: "Landline " + LandlineNumber + (BothOnFile && PrefersLandline ? " (preferred)" : string.Empty);

	/// <summary>
	/// The number to lead with, and the other one — the member's own
	/// answer to "which of these should be rung" decides the order.
	///
	/// <para>Ordering here rather than in the layout so a row is two
	/// labels each shown when it has something in it. Expressing "the
	/// landline, but only when it is not already above" in XAML takes a
	/// second condition the markup cannot spell without a converter, and
	/// getting it wrong leaves a blank line in the list rather than
	/// anything that looks like a bug.</para>
	///
	/// <para>A member with one number puts it in whichever slot the
	/// preference implies and leaves the other empty; only one label is
	/// showing either way, so the slot it lands in does not matter.</para>
	/// </summary>
	[JsonIgnore]
	public string FirstNumberLine => PrefersLandline ? LandlineLine : MobileLine;

	/// <inheritdoc cref="FirstNumberLine"/>
	[JsonIgnore]
	public string SecondNumberLine => PrefersLandline ? MobileLine : LandlineLine;

	/// <summary>
	/// What the row says when the numbers came back empty. Empty itself
	/// until they have been asked for, so nothing shows before then.
	/// </summary>
	[JsonIgnore]
	public string NoNumberNote => IsContactShown && !HasAnyNumber
		? "No number on file"
		: string.Empty;

	private bool BothOnFile => MobileNumber.Length > 0 && LandlineNumber.Length > 0;

	partial void OnMobileNumberChanged(string value) => OnContactLinesChanged();

	partial void OnLandlineNumberChanged(string value) => OnContactLinesChanged();

	partial void OnPreferredContactChanged(string value) => OnContactLinesChanged();

	partial void OnIsContactShownChanged(bool value) => OnPropertyChanged(nameof(NoNumberNote));

	private void OnContactLinesChanged()
	{
		OnPropertyChanged(nameof(HasAnyNumber));
		OnPropertyChanged(nameof(PrefersLandline));
		OnPropertyChanged(nameof(MobileLine));
		OnPropertyChanged(nameof(LandlineLine));
		OnPropertyChanged(nameof(FirstNumberLine));
		OnPropertyChanged(nameof(SecondNumberLine));
		OnPropertyChanged(nameof(NoNumberNote));
	}
}

/// <summary>
/// A committee, and everybody on it and under it.
///
/// <para><b>Addressed by <see cref="Slug"/>, never by a numeric id.</b>
/// The committee tree is built by hand in wp-admin on each site, so the
/// same committee has different term ids on dev, test and production —
/// an id a handset had cached would be right on one machine and point at
/// something else on the next.</para>
/// </summary>
public sealed class HandCommittee
{
	[JsonPropertyName("slug")]
	public string Slug { get; set; } = string.Empty;

	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// How deep in the tree this sits, so the list can indent rather than
	/// reading dashes out of a label.
	/// </summary>
	[JsonPropertyName("depth")]
	public int Depth { get; set; }

	/// <summary>
	/// How many handsets sending to it would reach — the whole branch,
	/// not just this node, because that is what sending to it does.
	/// </summary>
	[JsonPropertyName("handsets")]
	public int Handsets { get; set; }

	/// <summary>The count as the list says it.</summary>
	[JsonIgnore]
	public string HandsetsLine => Handsets == 1 ? "1 handset" : $"{Handsets} handsets";

	/// <summary>
	/// Indentation for the row, as the tree's shape rather than as
	/// punctuation in the name.
	/// </summary>
	[JsonIgnore]
	public double Indent => Depth * 16;

	/// <summary>
	/// A committee nobody can be reached on is shown and refused rather
	/// than hidden — same reasoning as <see cref="HandMember.Reachable"/>.
	/// </summary>
	[JsonIgnore]
	public bool Reachable => Handsets > 0;
}
