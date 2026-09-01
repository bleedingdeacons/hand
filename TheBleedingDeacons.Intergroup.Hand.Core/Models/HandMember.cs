using System.Text.Json.Serialization;

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
/// </summary>
public sealed class HandMember
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
