using System.Globalization;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// One alert as Reach sends it.
///
/// <para>Alerts carry no personal data by design — Reach refuses to put
/// any in them, because the text ends up on a lock screen and passes
/// through Google's push infrastructure on the way. So this type holds a
/// title, a body and a reference to look the real details up by, and
/// nothing that would identify a caller.</para>
///
/// <para>The same shape arrives by two routes: parsed from the JSON body
/// of a poll, and reassembled from the string map sealed inside an FCM
/// data message. <see cref="FromPushData"/> is the second of those,
/// which is why every field has to survive a round trip through
/// strings.</para>
///
/// <para>Only the push is encrypted. The poll is HTTPS straight to our
/// own server and the payload key exists to keep content away from
/// Google's push infrastructure, which the poll never touches — which is
/// also what lets the poll go on working as the fallback when a
/// handset's key is broken.</para>
/// </summary>
public partial class HandAlert : ObservableObject
{
	public const string PriorityUrgent = "urgent";

	/// <summary>
	/// The one kind that is an instruction rather than an alert: Reach
	/// sends it as an administrator removes this handset from the rota.
	///
	/// <para>It must never reach the alarm. Everything else here is
	/// something a responder is being asked to act on; this is the app
	/// being told it is no longer enrolled, and waking someone at 3am to
	/// read it would be absurd. <c>AlertService</c> intercepts it before
	/// admission and signs out instead — see <c>IsDeviceRemoval</c>.</para>
	///
	/// <para>Matches the kind Reach raises in
	/// <c>DevicesPage::removeFromRequest()</c>. The two spellings are a
	/// wire contract; changing one without the other silently turns the
	/// notice back into an alarm.</para>
	/// </summary>
	public const string KindDeviceRemoved = "device_removed";

	/// <summary>
	/// Whether Reach holds contact details for this alert.
	///
	/// <para>A flag, never the details. Those are personal data and stay
	/// off the push and the poll entirely — see <see cref="Contact"/>.</para>
	/// </summary>
	[JsonPropertyName("has_contact")]
	public bool HasContact { get; set; }

	/// <summary>
	/// The contact details, once a responder has asked for them.
	///
	/// <para>Empty until then, and deliberately so: this arrives from a
	/// separate authenticated request that Reach writes an audit entry
	/// for. It is never in the push payload and never on the lock screen,
	/// which is the whole reason it is fetched rather than delivered.</para>
	/// </summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial string Contact { get; set; } = string.Empty;

	/// <summary>Whether the fetch is in flight, so the UI can say so.</summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial bool IsLoadingContact { get; set; }

	/// <summary>Whether there is a contact on screen right now.</summary>
	[JsonIgnore]
	public bool IsContactShown => Contact.Length > 0;

	partial void OnContactChanged(string value) => OnPropertyChanged(nameof(IsContactShown));

	[JsonPropertyName("id")]
	public long Id { get; set; }

	/// <summary>What kind of thing happened, e.g. <c>shift_uncovered</c>.</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = string.Empty;

	/// <summary>Which plugin raised it.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; } = string.Empty;

	[JsonPropertyName("priority")]
	public string Priority { get; set; } = "normal";

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;

	/// <summary>
	/// The raiser's own reference, e.g. <c>SHIFT-2026-08-15-N</c>. This is
	/// what a responder quotes to find the details through a channel that
	/// is actually private.
	/// </summary>
	[JsonPropertyName("reference")]
	public string Reference { get; set; } = string.Empty;

	[JsonPropertyName("created_at")]
	public long CreatedAt { get; set; }

	[JsonPropertyName("expires_at")]
	public long ExpiresAt { get; set; }

	/// <summary>
	/// Whatever the raising plugin attached, as a string map.
	///
	/// <para>Read through <see cref="AlertPayloadConverter"/> rather than
	/// the built-in dictionary converter, which throws on the shapes
	/// Reach genuinely sends — see that type for why one odd payload must
	/// not be allowed to cost the whole poll.</para>
	/// </summary>
	[JsonPropertyName("payload")]
	[JsonConverter(typeof(AlertPayloadConverter))]
	public Dictionary<string, string> Payload { get; set; } = new(StringComparer.Ordinal);

	public bool IsUrgent =>
		string.Equals(Priority, PriorityUrgent, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// What a secure lock screen is <i>offered</i> in place of the alert.
	///
	/// <para>Reach already refuses to put personal data in an alert, so in
	/// principle <see cref="Title"/> is safe to display. This does not rely
	/// on that. A responder's phone lies face-up on a table in a room with
	/// other people in it, and the one field a human writes freehand — the
	/// administrator's custom message — is validated for length and markup
	/// but not for meaning. Redacting whatever the payload happens to hold
	/// costs a tap to read and removes the whole question.</para>
	///
	/// <para><b>Offered, not imposed — and the difference matters.</b> The
	/// Android presenter marks the notification private and hands the
	/// system this as its public version, which is the whole of what an
	/// app may do. Android substitutes it only where the phone's owner has
	/// chosen to hide sensitive content; where they have chosen to show
	/// everything, which is the default on many devices, the alert's own
	/// words go on the lock screen and nothing here can stop it. So this
	/// property is what a redacted lock screen <i>would</i> say, not a
	/// promise about what a given handset shows. Hand reports which it is
	/// doing — see <c>ILockScreenPrivacy</c> — so an intergroup can see
	/// the handsets that are reading alerts out to the room.</para>
	///
	/// <para>Deliberately carries nothing from the payload at all, not even
	/// <see cref="Reference"/>. A reference is non-identifying by design and
	/// would be genuinely useful here, but "by design" is exactly the
	/// assurance this property exists not to depend on.</para>
	///
	/// <para>Urgency is the one thing it does keep, because urgency is not a
	/// secret and it is what tells a responder whether the phone can wait
	/// until they have finished what they are doing.</para>
	/// </summary>
	public string LockScreenTitle =>
		IsUrgent ? "Urgent helpline alert" : "Helpline alert";

	/// <summary>
	/// The second line of <see cref="LockScreenTitle"/>'s notification.
	/// Constant rather than computed: there is nothing about an alert that
	/// may safely vary it.
	/// </summary>
	public const string LockScreenBody = "Unlock to read";

	/// <summary>
	/// Whether this is the removal notice rather than an alert. See
	/// <see cref="KindDeviceRemoved"/>.
	/// </summary>
	public bool IsDeviceRemoval =>
		string.Equals(Kind, KindDeviceRemoved, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether the alert's window has closed. Checked before alarming as
	/// well as when polling: a push can be delivered late, and a handset
	/// that has been out of signal should not start shouting about
	/// something that stopped mattering an hour ago.
	/// </summary>
	public bool IsExpired(DateTimeOffset now) =>
		ExpiresAt > 0 && now.ToUnixTimeSeconds() >= ExpiresAt;

	/// <summary>
	/// Rebuild an alert from an FCM data payload.
	/// </summary>
	/// <remarks>
	/// <para><b>The payload is one sealed blob.</b> Reach encrypts the
	/// whole data map to this handset's own key and sends it as a single
	/// <c>ciphertext</c> field, so everything below is read out of what
	/// that opens into rather than off the push itself. Nothing readable
	/// crosses Google, whatever the alert happens to contain.</para>
	///
	/// <para><b>Null is the answer to every fault, and there are three of
	/// them.</b> A push with no <c>ciphertext</c> did not come from a
	/// server that knows this handset's key, and is not a legitimate
	/// message; one that will not open means the key here is wrong; one
	/// that opens without a usable id could never be acknowledged and
	/// would ring until the battery went. None is shown to the responder.
	/// The caller reports the fault so a broken handset appears on
	/// Reach's devices screen instead of going quiet — see
	/// <c>HandFirebaseMessagingService.OnMessageReceived</c> — and the
	/// poll, which is unencrypted HTTPS to our own server, still delivers
	/// the alert by the slower route.</para>
	///
	/// <para>Inside the blob everything is still a string, because FCM's
	/// data block is a string→string map and the server builds the sealed
	/// JSON from the same shape. So the numbers still have to be parsed
	/// back, under the invariant culture: these values were written by the
	/// server, not by the person holding the phone, and parsing them under
	/// the device's locale would make a handset in a locale with different
	/// digit conventions read them differently from every other handset on
	/// the rota.</para>
	/// </remarks>
	public static HandAlert? FromPushData(IDictionary<string, string> data, string payloadKey = "")
	{
		ArgumentNullException.ThrowIfNull(data);

		if (!data.TryGetValue("ciphertext", out var sealedPayload) || sealedPayload.Length == 0)
		{
			return null;
		}

		var opened = AlertPayloadCipher.Open(sealedPayload, payloadKey);
		if (opened is null)
		{
			return null;
		}

		if (!opened.TryGetValue("alert_id", out var rawId)
			|| !long.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
			|| id <= 0)
		{
			return null;
		}

		var alert = new HandAlert
		{
			Id = id,
			Kind = Value(opened, "kind"),
			Source = Value(opened, "source"),
			Priority = Value(opened, "priority"),
			Title = Value(opened, "title"),
			Body = Value(opened, "body"),
			Reference = Value(opened, "reference"),
			CreatedAt = Number(opened, "created_at"),
			ExpiresAt = Number(opened, "expires_at"),
			HasContact = Value(opened, "has_contact") is "1" or "true",
		};

		// Anything the raising plugin added travels alongside the fields
		// above. The reserved names are dropped so a plugin's own "title"
		// does not reappear as a payload entry.
		foreach (var pair in opened)
		{
			if (!ReservedKeys.Contains(pair.Key))
			{
				alert.Payload[pair.Key] = pair.Value;
			}
		}

		return alert;
	}

	private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal)
	{
		"alert_id", "kind", "source", "priority", "title", "body",
		"reference", "created_at", "expires_at", "channel", "sound",
		"has_contact",

		// Not a field the server puts inside the blob — it is the blob's
		// own name, out on the push. Reserved anyway because the server
		// merges a plugin's extras into the map it seals, so a plugin
		// with an extra of this name would otherwise land a stray copy
		// wherever payload entries are displayed.
		"ciphertext",
	};

	private static string Value(IDictionary<string, string> data, string key) =>
		data.TryGetValue(key, out var value) ? value : string.Empty;

	private static long Number(IDictionary<string, string> data, string key) =>
		data.TryGetValue(key, out var value)
		&& long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0;
}
