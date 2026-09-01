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
	/// The loudest level: full-screen intent, alarm category, looping
	/// siren, and a card the colour of a fire door. For what has to be
	/// dealt with now.
	///
	/// <para>The spelling is a wire contract shared with
	/// <c>Alert::LEVEL_RED</c> in Reach.</para>
	/// </summary>
	public const string LevelRed = "red";

	/// <summary>
	/// Audible but not commanding: a heads-up notification with a sound,
	/// which a responder can miss and catch up with. The default, and the
	/// right level for most things.
	/// </summary>
	public const string LevelYellow = "yellow";

	/// <summary>
	/// Information and reminders. The tray, at ordinary importance, and
	/// never a siren — see <see cref="IsQuiet"/>.
	/// </summary>
	public const string LevelBlue = "blue";

	/// <summary>
	/// The first responder to acknowledge takes this on. Everybody else
	/// is told who answered and loses the card; this handset's button
	/// says Acknowledge.
	///
	/// <para>The spelling is a wire contract shared with
	/// <c>Alert::RESPONSE_FIRST</c> in Reach.</para>
	/// </summary>
	public const string ResponseFirst = "first";

	/// <summary>
	/// Everybody reads it and closes their own copy. Nobody is taking
	/// anything on, so the button says Close and closing it leaves the
	/// message on every other handset.
	/// </summary>
	public const string ResponseNone = "none";

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
	/// The kind that reports on another message rather than raising one:
	/// Reach sends it when a handset acknowledges, to everybody else the
	/// message went to, saying who picked it up.
	///
	/// <para><b>It must never alarm.</b> The whole of its content is that
	/// somebody has already dealt with the thing that did alarm, and
	/// waking a second responder to tell them the first one answered
	/// would be worse than saying nothing. So it is admitted quietly —
	/// see <see cref="IsQuiet"/> — and its card offers Close rather than
	/// Acknowledge, because there is nothing here to acknowledge.</para>
	///
	/// <para>Matches <c>Alert::KIND_ACKNOWLEDGED</c> in Reach. Like
	/// <see cref="KindDeviceRemoved"/> the two spellings are a wire
	/// contract; changing one alone turns the notice back into a
	/// siren.</para>
	/// </summary>
	public const string KindMessageAcknowledged = "message_acknowledged";

	/// <summary>
	/// A responder's reply, carried back to whoever raised the original.
	///
	/// <para>Like the acknowledgement notice it is quiet by its level and
	/// response rather than by its kind, so nothing branches on this to
	/// decide how loud it is. What the kind decides is that it cannot be
	/// replied to — see <see cref="IsNotice"/>.</para>
	///
	/// <para>Matches <c>Alert::KIND_REPLY</c> in Reach; the two spellings
	/// are a wire contract.</para>
	/// </summary>
	public const string KindMessageReply = "message_reply";

	/// <summary>
	/// Payload key naming the message a notice is about. Written by
	/// <c>AcknowledgementNotifier::PAYLOAD_MESSAGE_UUID</c>.
	/// </summary>
	public const string PayloadAckMessageUuid = "ack_message_uuid";

	/// <summary>
	/// Payload key naming who acknowledged. A Unity anonymous name, never
	/// an email address — see <c>AcknowledgementNotifier</c> on why the
	/// usual fall back to the address is wrong for something that reaches
	/// a lock screen.
	/// </summary>
	public const string PayloadAckResponder = "ack_responder";

	/// <summary>What a notice says when it names nobody.</summary>
	public const string UnknownResponder = "Another responder";

	/// <summary>
	/// Whether Reach holds contact details for this alert.
	///
	/// <para>A flag, never the details. Those are personal data and stay
	/// off the push and the poll entirely — see <see cref="Contact"/>.</para>
	///
	/// <para><b>Observable, because it can be corrected after the card is
	/// on screen.</b> A push cannot always say whether a contact exists —
	/// an older Reach omitted the flag entirely — so the poll copy that
	/// arrives seconds later may know better, and
	/// <see cref="Services.AlertService"/> promotes it. A plain property
	/// would have been set without the button ever appearing, which is
	/// the same bug wearing a different hat.</para>
	/// </summary>
	[JsonPropertyName("has_contact")]
	[ObservableProperty]
	public partial bool HasContact { get; set; }

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

	/// <summary>
	/// The send this alert is one delivery of.
	///
	/// <para>An id identifies a row; this identifies the thing somebody
	/// sent. Usually the two are the same — a broadcast is one row
	/// addressed to everybody — but an administrator messaging a
	/// responder who holds a phone and a tablet raises two alerts on
	/// purpose, so that each handset carries its own acknowledgement, and
	/// only this says they are one message.</para>
	///
	/// <para>What Hand needs it for: matching an acknowledgement notice
	/// to the alert it is about. The notice cannot quote an id, because
	/// the id it would quote belongs to whichever copy the other
	/// responder happened to answer. See <see cref="AcknowledgesMessage"/>.</para>
	///
	/// <para>Empty on an alert raised before Reach had the column.
	/// Nothing matches on an empty uuid.</para>
	/// </summary>
	[JsonPropertyName("message_uuid")]
	public string MessageUuid { get; set; } = string.Empty;

	/// <summary>What kind of thing happened, e.g. <c>shift_uncovered</c>.</summary>
	[JsonPropertyName("kind")]
	public string Kind { get; set; } = string.Empty;

	/// <summary>Which plugin raised it.</summary>
	[JsonPropertyName("source")]
	public string Source { get; set; } = string.Empty;

	/// <summary>
	/// The older, two-value spelling of <see cref="Level"/>, kept because
	/// Reach still sends it and an older server sends nothing else.
	///
	/// <para>Nothing in the app branches on it any more — see
	/// <see cref="IsUrgent"/>, which asks the level. It survives so that a
	/// handset on this build talking to a Reach that predates the level
	/// still knows a red alert when one arrives: see
	/// <see cref="LevelOrDerived"/>.</para>
	/// </summary>
	[JsonPropertyName("priority")]
	public string Priority { get; set; } = "normal";

	/// <summary>
	/// How loud this alert is, and what colour its card is.
	///
	/// <para>One of <see cref="LevelRed"/>, <see cref="LevelYellow"/> or
	/// <see cref="LevelBlue"/>. The spellings are a wire contract shared
	/// with <c>Alert::LEVEL_*</c> in Reach.</para>
	///
	/// <para><b>Empty is normal and is not a fault.</b> A Reach that
	/// predates the level sends no such field, and this is the value that
	/// says so. Read it through <see cref="LevelOrDerived"/>, never
	/// directly, so that case falls back to the priority instead of
	/// reading as an unrecognised level.</para>
	/// </summary>
	[JsonPropertyName("level")]
	public string Level { get; set; } = string.Empty;

	/// <summary>
	/// Whether somebody has to take this on, or everybody just reads it.
	///
	/// <para><see cref="ResponseFirst"/> or <see cref="ResponseNone"/>;
	/// the spellings are a wire contract shared with
	/// <c>Alert::RESPONSE_*</c> in Reach. Defaults to first-to-respond,
	/// because that is what every alert was before the field existed and
	/// what an older Reach still means by sending nothing.</para>
	/// </summary>
	[JsonPropertyName("response")]
	public string Response { get; set; } = ResponseFirst;

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

	/// <summary>
	/// The level this alert is at, falling back to the priority where the
	/// server did not send one.
	///
	/// <para><b>Everything reads this and nothing reads <see cref="Level"/>
	/// directly.</b> A Reach that predates the level sends only a
	/// priority, and treating its absent level as "unrecognised, call it
	/// yellow" would silently demote every urgent alert that server
	/// raises to a heads-up — on the one route where the handset is
	/// newer than the server, which is the ordinary way round for an app
	/// that updates itself.</para>
	/// </summary>
	public string LevelOrDerived
	{
		get
		{
			if (string.Equals(Level, LevelRed, StringComparison.OrdinalIgnoreCase))
			{
				return LevelRed;
			}

			if (string.Equals(Level, LevelBlue, StringComparison.OrdinalIgnoreCase))
			{
				return LevelBlue;
			}

			if (string.Equals(Level, LevelYellow, StringComparison.OrdinalIgnoreCase))
			{
				return LevelYellow;
			}

			// No level, or one this build has never heard of. The priority
			// is the only other thing that speaks to loudness, and an
			// unrecognised level is safer read as the middle rung than as
			// silence.
			return IsUrgentPriority ? LevelRed : LevelYellow;
		}
	}

	/// <summary>Whether this is the loudest level. See <see cref="LevelRed"/>.</summary>
	public bool IsUrgent =>
		string.Equals(LevelOrDerived, LevelRed, StringComparison.Ordinal);

	/// <summary>Whether this is the middle rung. See <see cref="LevelYellow"/>.</summary>
	public bool IsWarning =>
		string.Equals(LevelOrDerived, LevelYellow, StringComparison.Ordinal);

	/// <summary>
	/// Whether the older two-value field says urgent. Only
	/// <see cref="LevelOrDerived"/> should ask.
	/// </summary>
	private bool IsUrgentPriority =>
		string.Equals(Priority, PriorityUrgent, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether everybody reads this and closes their own copy, rather
	/// than one responder taking it on. See <see cref="ResponseNone"/>.
	/// </summary>
	public bool IsInformational =>
		string.Equals(Response, ResponseNone, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// The card's background, as a hex colour.
	///
	/// <para><b>A string rather than a <c>Color</c>, and deliberately.</b>
	/// This project has no MAUI workload — see the csproj — so a
	/// <c>Microsoft.Maui.Graphics.Color</c> would not compile here, and
	/// putting the palette in the app project instead would put it where
	/// nothing can test it. XAML converts the string on binding.</para>
	///
	/// <para>The three colours are the level, and that is the whole point
	/// of the level: a responder should be able to tell a callback from a
	/// reminder across a room, before reading a word.</para>
	/// </summary>
	public string LevelBackground => LevelOrDerived switch
	{
		LevelRed => "#B3261E",
		LevelBlue => "#1565C0",
		_ => "#F9A825",
	};

	/// <summary>
	/// What is drawn on <see cref="LevelBackground"/>: white, on every
	/// level.
	///
	/// <para>Still a property rather than a literal in the XAML, because
	/// the card binds its text, its button and its border to it in five
	/// places. One of them left behind when a level is added or a colour
	/// changes is a card with a stray white label on a field that no
	/// longer suits it.</para>
	///
	/// <para>Yellow briefly took near-black here, on the grounds that
	/// white on <c>#F9A825</c> is thin. It reads as a different component
	/// rather than a warmer one of the same kind — one card in three
	/// inverting its text and its button is more jarring than the contrast
	/// is worth. Uniform white it is; if yellow needs more contrast the
	/// answer is a deeper amber in <see cref="LevelBackground"/>, which
	/// keeps all three cards the same shape.</para>
	/// </summary>
	public string LevelForeground => "#FFFFFF";

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
	/// Whether this reports somebody else's acknowledgement rather than
	/// asking for one. See <see cref="KindMessageAcknowledged"/>.
	/// </summary>
	public bool IsAcknowledgementNotice =>
		string.Equals(Kind, KindMessageAcknowledged, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether this is Reach talking about another alert rather than
	/// something in its own right.
	///
	/// <para>Mirrors <c>Alert::isNotice()</c> on the server, which is the
	/// authority — the reply route refuses both kinds outright.</para>
	/// </summary>
	public bool IsNotice =>
		IsAcknowledgementNotice
		|| string.Equals(Kind, KindMessageReply, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Whether this card should offer Reply.
	///
	/// <para><b>Not on a notice.</b> The server refuses a reply to one —
	/// otherwise an answered call becomes an unbounded exchange between
	/// two handsets — so offering the button anyway gave a responder
	/// something that looked like it worked, took their words, and threw
	/// them away against a 404. A button that cannot succeed is worse
	/// than no button.</para>
	///
	/// <para>The alert a notice reports on is still repliable; it is
	/// reached from the history, where it survives being cleared off the
	/// screen. See <see cref="AlertHistoryEntry.CanReply"/>.</para>
	/// </summary>
	public bool CanReply => !IsNotice;

	/// <summary>
	/// Whether this may be shown without waking anybody: in the tray at
	/// ordinary importance, no siren, no full-screen intent.
	///
	/// <para>Expressed as its own property rather than as a test at each
	/// call site, because three platform presenters and the alert loop
	/// all have to agree on it and only one of them has tests.</para>
	///
	/// <para><b>It asks the level now, not the kind.</b> This used to mean
	/// "is an acknowledgement notice", that being the only thing quiet
	/// enough to want it. <see cref="LevelBlue"/> is the same property
	/// made askable, and the notice is now simply one of the things that
	/// asks — it is raised as blue by Reach rather than recognised as
	/// special here.</para>
	/// </summary>
	public bool IsQuiet =>
		string.Equals(LevelOrDerived, LevelBlue, StringComparison.Ordinal);

	/// <summary>
	/// The message a notice is about, or empty when this is not one.
	/// Matched against <see cref="MessageUuid"/>.
	/// </summary>
	public string AcknowledgesMessage =>
		IsAcknowledgementNotice && Payload.TryGetValue(PayloadAckMessageUuid, out var uuid)
			? uuid
			: string.Empty;

	/// <summary>
	/// Who acknowledged, as a notice reports it. Falls back to the
	/// generic name rather than to nothing: a notice that named nobody
	/// would read as a fault.
	/// </summary>
	public string AcknowledgedByName
	{
		get
		{
			if (Payload.TryGetValue(PayloadAckResponder, out var name) && name.Length > 0)
			{
				return name;
			}

			return UnknownResponder;
		}
	}

	/// <summary>
	/// Whether this responder has taken this job on.
	///
	/// <para><b>The card stays on screen after it is acknowledged, and
	/// this is what says so.</b> Pressing Acknowledge used to remove it
	/// outright, which took the reference and the Show contact button
	/// away at exactly the moment they became useful — the responder has
	/// just accepted a call and now needs the details to make it. So
	/// acknowledging silences the alarm, tells Reach, and leaves the card
	/// where it is; the second press closes it.</para>
	///
	/// <para>Only ever set on the handset that pressed the button. The
	/// other handsets do not mark this alert as answered, they lose it —
	/// see <c>AlertService.RemoveMessageAsync</c>.</para>
	/// </summary>
	[JsonIgnore]
	[ObservableProperty]
	public partial bool AcknowledgedHere { get; set; }

	/// <summary>
	/// Whether there is nothing outstanding about this card: this
	/// handset has taken the job, or the card was never a job.
	///
	/// <para>What the alarm counts. One alarm serves any number of
	/// outstanding alerts and stops when the last is answered — and an
	/// acknowledged card that stays on screen must not keep it
	/// ringing.</para>
	///
	/// <para><b>It asks the response requirement now, not the kind.</b>
	/// Same move as <see cref="IsQuiet"/>: an acknowledgement notice was
	/// the only thing nobody had to take on, and
	/// <see cref="ResponseNone"/> is that made askable.</para>
	/// </summary>
	[JsonIgnore]
	public bool IsSettled => AcknowledgedHere || IsInformational;

	/// <summary>The line an acknowledged card shows in place of nothing.</summary>
	[JsonIgnore]
	public string AnsweredLine => AcknowledgedHere ? "Acknowledged by you" : string.Empty;

	/// <summary>
	/// What the card's button says.
	///
	/// <para>Acknowledge means "I have this". A message nobody has to
	/// take on is not a job, and a card this responder has already taken
	/// cannot be taken again — so both offer Close, which removes the
	/// card and nothing else.</para>
	///
	/// <para>Unchanged when the response requirement arrived, which is
	/// the sign the seam was in the right place: it reads
	/// <see cref="IsSettled"/>, and that started following the
	/// requirement instead of the kind.</para>
	/// </summary>
	[JsonIgnore]
	public string ActionLabel => IsSettled ? "Close" : "Acknowledge";

	partial void OnAcknowledgedHereChanged(bool value)
	{
		OnPropertyChanged(nameof(IsSettled));
		OnPropertyChanged(nameof(AnsweredLine));
		OnPropertyChanged(nameof(ActionLabel));
	}

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
			Level = Value(opened, "level"),
			// Absent on a push from a Reach that predates the field, and
			// first-to-respond is what every alert meant then. Read
			// through the property default rather than as an empty string,
			// which would make every pushed alert informational and turn
			// every Acknowledge button into a Close.
			Response = Value(opened, "response") is { Length: > 0 } response
				? response
				: ResponseFirst,
			Title = Value(opened, "title"),
			Body = Value(opened, "body"),
			Reference = Value(opened, "reference"),
			MessageUuid = Value(opened, "message_uuid"),
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
		"alert_id", "message_uuid", "kind", "source", "priority", "level",
		"response", "title", "body", "reference", "created_at", "expires_at",
		"channel", "sound", "has_contact",

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
