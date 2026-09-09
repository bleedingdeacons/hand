using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// One member's phone numbers, as Reach answers for them.
///
/// <para><b>Separate from <see cref="HandMember"/> on purpose.</b> The
/// picker's rows arrive from a route that carries no numbers at all, and
/// these come from a second request made only when a responder asks —
/// see
/// <see cref="Services.Interfaces.IReachClient.GetMemberContactAsync"/>.
/// Keeping the wire shapes apart is what stops a future change to the
/// list deserialiser quietly acquiring somewhere to put numbers.</para>
///
/// <para>Both numbers can be empty: a member Unity holds no number for
/// is a perfectly good answer, not a failure, and the picker says so
/// rather than offering to ask again.</para>
/// </summary>
public sealed class HandMemberContact
{
	/// <summary>
	/// The member these belong to, echoed back so a late reply can be
	/// matched to the row that asked rather than to whatever is selected
	/// by the time it lands.
	/// </summary>
	[JsonPropertyName("id")]
	public long Id { get; set; }

	[JsonPropertyName("mobile_number")]
	public string MobileNumber { get; set; } = string.Empty;

	[JsonPropertyName("landline_number")]
	public string LandlineNumber { get; set; } = string.Empty;

	/// <summary>
	/// Which number the member asked to be rung on — "Mobile" or
	/// "Landline", as Reach's own enum spells it.
	///
	/// <para>Only meaningful when both numbers are present. Reach keeps
	/// a member's last saved choice even for a landline since deleted,
	/// so this is read together with the numbers rather than trusted
	/// alone — see <see cref="HandMember.PrefersLandline"/>.</para>
	/// </summary>
	[JsonPropertyName("preferred_contact")]
	public string PreferredContact { get; set; } = string.Empty;
}
