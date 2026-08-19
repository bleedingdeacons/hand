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
/// of a poll, and reassembled from the string map in an FCM data
/// message. <see cref="FromPushData"/> is the second of those, which is
/// why every field has to survive a round trip through strings.</para>
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

	[JsonPropertyName("payload")]
	public Dictionary<string, string> Payload { get; set; } = new(StringComparer.Ordinal);

	public bool IsUrgent =>
		string.Equals(Priority, PriorityUrgent, StringComparison.OrdinalIgnoreCase);

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
	/// FCM's data block is a string→string map, so every value arrives as
	/// text and the numbers have to be parsed back. A message that has
	/// lost its id is not usable — the id is what the acknowledgement is
	/// keyed on, and an alert that cannot be acknowledged would ring
	/// forever — so that case returns null and the poll picks the alert
	/// up properly instead.
	/// </remarks>
	public static HandAlert? FromPushData(IDictionary<string, string> data)
	{
		ArgumentNullException.ThrowIfNull(data);

		// Invariant culture throughout: these values were written by the
		// server, not by the person holding the phone. Parsing them under
		// the device's locale would make a handset in a locale with
		// different digit or separator conventions read them differently
		// from every other handset on the rota.
		if (!data.TryGetValue("alert_id", out var rawId)
			|| !long.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
			|| id <= 0)
		{
			return null;
		}

		var alert = new HandAlert
		{
			Id = id,
			Kind = Value(data, "kind"),
			Source = Value(data, "source"),
			Priority = Value(data, "priority"),
			Title = Value(data, "title"),
			Body = Value(data, "body"),
			Reference = Value(data, "reference"),
			CreatedAt = Number(data, "created_at"),
			ExpiresAt = Number(data, "expires_at"),
			HasContact = Value(data, "has_contact") is "1" or "true",
		};

		// Anything the raising plugin added travels alongside the fields
		// above. The reserved names are dropped so a plugin's own "title"
		// does not reappear as a payload entry.
		foreach (var pair in data)
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
	};

	private static string Value(IDictionary<string, string> data, string key) =>
		data.TryGetValue(key, out var value) ? value : string.Empty;

	private static long Number(IDictionary<string, string> data, string key) =>
		data.TryGetValue(key, out var value)
		&& long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: 0;
}
