using System.Text.Json;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The alert as it arrives, by both routes.
///
/// <para>The push route gets the closer look, because it is the one where
/// every value has been flattened to a string on the way through FCM and
/// has to survive being parsed back. A field that comes back wrong there
/// is a handset that rings about the wrong thing, or does not ring.</para>
/// </summary>
public sealed class HandAlertTests
{
	/// <summary>
	/// The key these tests seal and open with. Fixed rather than random so
	/// a failure is reproducible.
	/// </summary>
	private static readonly byte[] TestKey = System.Text.Encoding.UTF8.GetBytes("0123456789abcdef0123456789abcdef");

	private static string TestKeyBase64 => Convert.ToBase64String(TestKey);

	/// <summary>
	/// Seal a whole data map the way Reach does — gzip, then AES-256-GCM,
	/// nonce then tag then ciphertext, base64 — so a test can send the
	/// shape a handset actually receives.
	///
	/// <para>Built here rather than by calling the app's own cipher: a
	/// test that sealed with the code under test would pass just as
	/// happily if both ends of the format changed together, and the two
	/// ends ship from different repositories.</para>
	/// </summary>
	private static string Seal(IDictionary<string, string> payload, byte[]? key = null)
	{
		var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);

		using var compressed = new MemoryStream();
		using (var gzip = new System.IO.Compression.GZipStream(
			compressed, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
		{
			var raw = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
			gzip.Write(raw, 0, raw.Length);
		}

		var plaintext = compressed.ToArray();
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using var gcm = new System.Security.Cryptography.AesGcm(key ?? TestKey, 16);
		gcm.Encrypt(nonce, plaintext, ciphertext, tag);

		return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
	}

	/// <summary>
	/// A push as it arrives: one sealed blob and nothing beside it.
	/// </summary>
	private static Dictionary<string, string> Push(IDictionary<string, string> payload) =>
		new(StringComparer.Ordinal) { ["ciphertext"] = Seal(payload) };

	/// <summary>The sealed map for an ordinary alert.</summary>
	private static Dictionary<string, string> Payload(long id = 4242) =>
		new(StringComparer.Ordinal)
		{
			["alert_id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
			["kind"] = "shift_uncovered",
			["source"] = "trusted",
			["priority"] = "urgent",
			["title"] = "Shift uncovered",
			["body"] = "Nobody is on the helpline.",
			["reference"] = "SHIFT-2026-08-15-N",
			["created_at"] = "1755250000",
			["expires_at"] = "1755253600",
			["has_contact"] = "1",
		};

	/// <summary>
	/// The message uuid has to survive the push as well as the poll: it
	/// is what an acknowledgement notice matches an alert on, and on
	/// Android the whole data map makes the trip sealed, as strings.
	/// </summary>
	[Fact]
	public void FromPushData_ReadsTheMessageUuid()
	{
		var payload = Payload();
		payload["message_uuid"] = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b";

		var alert = HandAlert.FromPushData(Push(payload), TestKeyBase64);

		Assert.NotNull(alert);
		Assert.Equal("3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b", alert.MessageUuid);

		// Reserved, so it does not also turn up as one of the raising
		// plugin's own extras wherever those are shown.
		Assert.DoesNotContain("message_uuid", alert.Payload.Keys);
	}

	/// <summary>
	/// A notice arrives by push like anything else, and what it reports
	/// travels as ordinary payload properties — which is what lets it
	/// through a wire format that carries nothing but strings.
	/// </summary>
	[Fact]
	public void FromPushData_RebuildsAnAcknowledgementNotice()
	{
		var payload = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "9",
			["message_uuid"] = "notice-9",
			["kind"] = HandAlert.KindMessageAcknowledged,
			// As Reach raises it. The notice is quiet because it is blue
			// and offers Close because nobody has to take it on — not
			// because anything here recognises the kind.
			["level"] = HandAlert.LevelBlue,
			["response"] = HandAlert.ResponseNone,
			["title"] = "Jo B acknowledged",
			[HandAlert.PayloadAckMessageUuid] = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b",
			[HandAlert.PayloadAckResponder] = "Jo B",
		};

		var alert = HandAlert.FromPushData(Push(payload), TestKeyBase64);

		Assert.NotNull(alert);
		Assert.True(alert.IsAcknowledgementNotice);

		// Quiet is the property every presenter branches on, and it is
		// the difference between a notification and a siren at 3am.
		Assert.True(alert.IsQuiet);

		// And its button says Close, because there is nothing to take on.
		Assert.True(alert.IsInformational);
		Assert.Equal("Close", alert.ActionLabel);

		Assert.Equal("3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b", alert.AcknowledgesMessage);
		Assert.Equal("Jo B", alert.AcknowledgedByName);
	}

	[Fact]
	public void FromPushData_ReadsEveryField()
	{
		var alert = HandAlert.FromPushData(Push(Payload()), TestKeyBase64);

		Assert.NotNull(alert);
		Assert.Equal(4242, alert.Id);
		Assert.Equal("shift_uncovered", alert.Kind);
		Assert.Equal("trusted", alert.Source);
		Assert.Equal("urgent", alert.Priority);
		Assert.Equal("Shift uncovered", alert.Title);
		Assert.Equal("Nobody is on the helpline.", alert.Body);
		Assert.Equal("SHIFT-2026-08-15-N", alert.Reference);
		Assert.Equal(1755250000, alert.CreatedAt);
		Assert.Equal(1755253600, alert.ExpiresAt);
		Assert.True(alert.HasContact);
		Assert.True(alert.IsUrgent);
	}

	/// <summary>
	/// The id is what an acknowledgement is keyed on, so a message without
	/// a usable one has to be refused outright — an alert that cannot be
	/// acknowledged would ring until the battery went.
	/// </summary>
	[Theory]
	[InlineData("")]
	[InlineData("not-a-number")]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("1.5")]
	public void FromPushData_RefusesUnusableId(string rawId)
	{
		var alert = HandAlert.FromPushData(
			Push(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = rawId,
				["title"] = "Shift uncovered",
			}),
			TestKeyBase64);

		Assert.Null(alert);
	}

	[Fact]
	public void FromPushData_RefusesAMessageWithNoIdAtAll() =>
		Assert.Null(HandAlert.FromPushData(
			Push(new Dictionary<string, string>(StringComparer.Ordinal)),
			TestKeyBase64));

	[Fact]
	public void FromPushData_RejectsNull() =>
		Assert.Throws<ArgumentNullException>(() => HandAlert.FromPushData(null!));

	[Fact]
	public void FromPushData_DefaultsEveryFieldItWasNotSent()
	{
		var alert = HandAlert.FromPushData(
			Push(new Dictionary<string, string>(StringComparer.Ordinal) { ["alert_id"] = "7" }),
			TestKeyBase64);

		Assert.NotNull(alert);
		Assert.Equal(string.Empty, alert.Kind);
		Assert.Equal(string.Empty, alert.Title);
		Assert.Equal(0, alert.CreatedAt);
		Assert.Equal(0, alert.ExpiresAt);
		Assert.False(alert.HasContact);
		Assert.Empty(alert.Payload);
	}

	/// <summary>
	/// A timestamp that will not parse must not become a plausible one.
	/// Zero reads as "no expiry", which is the safe way to be wrong: the
	/// alert rings rather than being silently discarded as stale.
	/// </summary>
	[Fact]
	public void FromPushData_TreatsAnUnparseableTimestampAsAbsent()
	{
		var alert = HandAlert.FromPushData(
			Push(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "7",
				["expires_at"] = "soon",
			}),
			TestKeyBase64);

		Assert.NotNull(alert);
		Assert.Equal(0, alert.ExpiresAt);
		Assert.False(alert.IsExpired(DateTimeOffset.UtcNow));
	}

	[Theory]
	[InlineData("1", true)]
	[InlineData("true", true)]
	[InlineData("0", false)]
	[InlineData("false", false)]
	[InlineData("yes", false)]
	[InlineData("", false)]
	public void FromPushData_ReadsHasContactAsTheServerSpellsIt(string raw, bool expected)
	{
		var alert = HandAlert.FromPushData(
			Push(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "7",
				["has_contact"] = raw,
			}),
			TestKeyBase64);

		Assert.NotNull(alert);
		Assert.Equal(expected, alert.HasContact);
	}

	/// <summary>
	/// The raising plugin's own extras come through, but the transport's
	/// own keys must not reappear as payload entries — a plugin's "title"
	/// would otherwise show up twice, once as the alert's and once as data.
	/// </summary>
	[Fact]
	public void FromPushData_KeepsPluginExtrasAndDropsReservedKeys()
	{
		var alert = HandAlert.FromPushData(
			Push(new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "7",
				["title"] = "Shift uncovered",
				["channel"] = "reach_alerts",
				["sound"] = "reach_alert",
				["rota_slot"] = "night",
				["region"] = "bristol",
			}),
			TestKeyBase64);

		Assert.NotNull(alert);
		Assert.Equal(
			["region", "rota_slot"],
			alert.Payload.Keys.Order(StringComparer.Ordinal));
		Assert.Equal("night", alert.Payload["rota_slot"]);
	}

	[Fact]
	public void IsExpired_IsFalseWhenThereIsNoExpiry() =>
		Assert.False(Alerts.New(expiresAt: 0).IsExpired(DateTimeOffset.UtcNow));

	[Fact]
	public void IsExpired_IsTrueOnTheSecondItExpires()
	{
		var now = DateTimeOffset.FromUnixTimeSeconds(1_755_250_000);
		var alert = Alerts.New(expiresAt: 1_755_250_000);

		Assert.True(alert.IsExpired(now));
		Assert.False(alert.IsExpired(now.AddSeconds(-1)));
	}

	[Theory]
	[InlineData("urgent", true)]
	[InlineData("URGENT", true)]
	[InlineData("Urgent", true)]
	[InlineData("normal", false)]
	[InlineData("", false)]
	public void IsUrgent_IgnoresCase(string priority, bool expected) =>
		Assert.Equal(expected, new HandAlert { Priority = priority }.IsUrgent);

	/// <summary>
	/// The removal notice is an instruction, not an alert, and the spelling
	/// is a wire contract with Reach — see the constant's own note.
	/// </summary>
	[Theory]
	[InlineData("device_removed", true)]
	[InlineData("DEVICE_REMOVED", true)]
	[InlineData("device removed", false)]
	[InlineData("shift_uncovered", false)]
	public void IsDeviceRemoval_MatchesTheWireSpellingOnly(string kind, bool expected) =>
		Assert.Equal(expected, new HandAlert { Kind = kind }.IsDeviceRemoval);

	/// <summary>
	/// The contact is fetched later and shown when it arrives, so the flag
	/// the page binds to has to change with it.
	/// </summary>
	[Fact]
	public void Contact_RaisesIsContactShown()
	{
		var alert = Alerts.New();
		var changed = new List<string?>();
		alert.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		Assert.False(alert.IsContactShown);

		alert.Contact = "07700 900000";

		Assert.True(alert.IsContactShown);
		Assert.Contains(nameof(HandAlert.Contact), changed, StringComparer.Ordinal);
		Assert.Contains(nameof(HandAlert.IsContactShown), changed, StringComparer.Ordinal);
	}

	[Fact]
	public void IsLoadingContact_NotifiesTheView()
	{
		var alert = Alerts.New();
		var changed = new List<string?>();
		alert.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

		alert.IsLoadingContact = true;

		Assert.Contains(nameof(HandAlert.IsLoadingContact), changed, StringComparer.Ordinal);
	}

	/// <summary>The poll route: the same alert, parsed from Reach's JSON.</summary>
	[Fact]
	public void Deserialises_FromReachJson()
	{
		const string json = """
			{
			  "id": 12,
			  "kind": "shift_uncovered",
			  "source": "trusted",
			  "priority": "urgent",
			  "title": "Shift uncovered",
			  "body": "Nobody is on the helpline.",
			  "reference": "SHIFT-2026-08-15-N",
			  "created_at": 1755250000,
			  "expires_at": 1755253600,
			  "has_contact": true,
			  "payload": { "rota_slot": "night" }
			}
			""";

		var alert = JsonSerializer.Deserialize<HandAlert>(json);

		Assert.NotNull(alert);
		Assert.Equal(12, alert.Id);
		Assert.True(alert.IsUrgent);
		Assert.True(alert.HasContact);
		Assert.Equal("night", alert.Payload["rota_slot"]);
	}

	/// <summary>
	/// Contact and its loading flag are view state fetched separately, and
	/// must never be written into anything that goes over the wire.
	/// </summary>
	[Fact]
	public void Serialises_WithoutTheContactDetails()
	{
		var alert = Alerts.New();
		alert.Contact = "07700 900000";
		alert.IsLoadingContact = true;

		var json = JsonSerializer.Serialize(alert);

		Assert.DoesNotContain("07700", json, StringComparison.Ordinal);
		Assert.DoesNotContain("isLoadingContact", json, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// A push carries one sealed blob and nothing beside it, and this is
	/// what opening it looks like end to end.
	/// </summary>
	[Fact]
	public void FromPushData_OpensASealedPush()
	{
		var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["ciphertext"] = Seal(
					new Dictionary<string, string>(StringComparer.Ordinal)
					{
						["alert_id"] = "9",
						["kind"] = "call_request",
						["title"] = "Callback wanted CR-9",
						["body"] = "Wanted in BS5",
						["reference"] = "CR-9",
						["area"] = "BS5",
					},
					key),
			},
			Convert.ToBase64String(key));

		Assert.NotNull(alert);
		Assert.Equal(9, alert.Id);
		Assert.Equal("call_request", alert.Kind);
		Assert.Equal("Callback wanted CR-9", alert.Title);
		Assert.Equal("Wanted in BS5", alert.Body);
		Assert.Equal("CR-9", alert.Reference);

		// The raising plugin's extras were sealed alongside the rest and
		// still come out as extras.
		Assert.Equal("BS5", alert.Payload["area"]);

		// The sealed blob's own name must not survive as a payload entry,
		// or the ciphertext would be shown wherever extras are displayed.
		Assert.False(alert.Payload.ContainsKey("ciphertext"));
	}

	/// <summary>
	/// A push with nothing sealed in it is not a legitimate message from
	/// this server, and is ignored outright.
	///
	/// <para>Reach seals the whole data map and sends nothing beside it,
	/// so there is no such thing as an unencrypted alert to fall back to
	/// reading. Building one from these fields would mean a handset
	/// happily displaying whatever anyone managed to push at it.</para>
	/// </summary>
	[Fact]
	public void FromPushData_IgnoresAnUnencryptedPush()
	{
		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "9",
				["kind"] = "call_request",
				["title"] = "Callback wanted for Joanne",
				["body"] = "Wanted in BS5",
			});

		Assert.Null(alert);
	}

	/// <summary>
	/// An empty <c>ciphertext</c> is the same as none at all — otherwise
	/// a truncated or half-built message would be read as one.
	/// </summary>
	[Fact]
	public void FromPushData_IgnoresAnEmptyCiphertext() =>
		Assert.Null(HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["ciphertext"] = string.Empty,
				["alert_id"] = "9",
			},
			TestKeyBase64));

	/// <summary>
	/// A push sealed to a key this handset does not hold is a failure, not
	/// a degraded alert.
	///
	/// <para>Nothing is shown: there is nothing to show, and the poll —
	/// HTTPS straight to our own server, unaffected by a bad payload key —
	/// still delivers the alert by the slower route. What must not happen
	/// is the handset going quiet unnoticed, which is why the caller
	/// reports the fault to Reach; see
	/// <c>HandFirebaseMessagingService.Refuse</c>.</para>
	/// </summary>
	[Fact]
	public void FromPushData_IgnoresAPushSealedToAnotherKey()
	{
		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["ciphertext"] = Seal(
					Payload(9),
					System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
			},
			TestKeyBase64);

		Assert.Null(alert);
	}

	/// <summary>
	/// GCM authenticates, so a payload altered on the way must fail rather
	/// than decrypt to something plausible.
	/// </summary>
	[Fact]
	public void FromPushData_IgnoresATamperedPush()
	{
		var bytes = Convert.FromBase64String(Seal(Payload(9)));
		bytes[^1] ^= 0xFF;

		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["ciphertext"] = Convert.ToBase64String(bytes),
			},
			TestKeyBase64);

		Assert.Null(alert);
	}

	/// <summary>A handset with no key at all cannot open anything.</summary>
	[Fact]
	public void FromPushData_IgnoresASealedPushWhenThisHandsetHasNoKey() =>
		Assert.Null(HandAlert.FromPushData(Push(Payload(9))));

	/// <summary>
	/// What a secure lock screen may show. The Android presenter hands these
	/// two values to the public version of the notification, so anything
	/// that leaked into them would be readable by whoever is standing near
	/// a phone lying face-up on a table.
	/// </summary>
	[Fact]
	public void LockScreenText_CarriesNothingFromThePayload()
	{
		var alert = Alerts.New();
		alert.Title = "Callback wanted for Joanne on 07700 900000";
		alert.Body = "She is at 14 Example Street";
		alert.Reference = "CR-000123";

		Assert.DoesNotContain("Joanne", alert.LockScreenTitle, StringComparison.Ordinal);
		Assert.DoesNotContain("07700", alert.LockScreenTitle, StringComparison.Ordinal);
		Assert.DoesNotContain("Example Street", alert.LockScreenTitle, StringComparison.Ordinal);
		Assert.DoesNotContain("CR-000123", alert.LockScreenTitle, StringComparison.Ordinal);

		Assert.DoesNotContain("Joanne", HandAlert.LockScreenBody, StringComparison.Ordinal);
		Assert.DoesNotContain("CR-000123", HandAlert.LockScreenBody, StringComparison.Ordinal);
	}

	// ── level ─────────────────────────────────────────────────────────

	/// <summary>
	/// The card is the colour of its level. Asserted on the model rather
	/// than through XAML because this is where the palette lives and the
	/// only place anything can test it.
	/// </summary>
	[Theory]
	[InlineData(HandAlert.LevelRed, "#B3261E")]
	[InlineData(HandAlert.LevelYellow, "#F9A825")]
	[InlineData(HandAlert.LevelBlue, "#1565C0")]
	public void TheCardIsTheColourOfItsLevel(string level, string background)
	{
		var alert = Alerts.New(level: level);

		Assert.Equal(background, alert.LevelBackground);

		// White on all three: the three cards differ by field colour and
		// nothing else, so none of them reads as a different component.
		Assert.Equal("#FFFFFF", alert.LevelForeground);
	}

	/// <summary>
	/// Only red alarms, and only blue is quiet. Yellow is neither: it
	/// makes a noise and can be missed, which is the whole reason there
	/// are three levels rather than two.
	/// </summary>
	[Theory]
	[InlineData(HandAlert.LevelRed, true, false)]
	[InlineData(HandAlert.LevelYellow, false, false)]
	[InlineData(HandAlert.LevelBlue, false, true)]
	public void TheLevelDecidesHowLoudTheHandsetIs(string level, bool urgent, bool quiet)
	{
		var alert = Alerts.New(level: level);

		Assert.Equal(urgent, alert.IsUrgent);
		Assert.Equal(quiet, alert.IsQuiet);
	}

	/// <summary>
	/// <b>A Reach that predates the level sends only a priority.</b>
	/// Reading its absent level as "unrecognised, call it yellow" would
	/// demote every urgent alert that server raises — on the one route
	/// where the handset is newer than the server, which is the ordinary
	/// way round for an app that updates itself.
	/// </summary>
	[Theory]
	[InlineData("urgent", HandAlert.LevelRed)]
	[InlineData("URGENT", HandAlert.LevelRed)]
	[InlineData("normal", HandAlert.LevelYellow)]
	[InlineData("", HandAlert.LevelYellow)]
	public void AnAlertWithNoLevelFallsBackToItsPriority(string priority, string expected)
	{
		var alert = Alerts.New();
		alert.Level = string.Empty;
		alert.Priority = priority;

		Assert.Equal(expected, alert.LevelOrDerived);
	}

	[Fact]
	public void AnUnrecognisedLevelIsReadAsTheMiddleRung()
	{
		// Never as silence: a level this build has not heard of might be
		// louder than anything it knows, and guessing quiet would be the
		// one mistake that loses an alert.
		var alert = Alerts.New();
		alert.Level = "puce";
		alert.Priority = "normal";

		Assert.Equal(HandAlert.LevelYellow, alert.LevelOrDerived);
		Assert.False(alert.IsQuiet);
	}

	[Fact]
	public void AnAlertWithNoResponseIsSomebodysToTakeOn()
	{
		// What an older Reach means by sending nothing, and what every
		// alert meant before the field existed. Read the other way round,
		// every Acknowledge button would silently become a Close.
		var alert = Alerts.New();
		alert.Response = string.Empty;

		Assert.False(alert.IsInformational);
		Assert.Equal("Acknowledge", alert.ActionLabel);
	}

	/// <summary>
	/// Urgency survives redaction deliberately: it is not a secret, and it
	/// is what tells a responder whether the phone can wait.
	/// </summary>
	[Theory]
	[InlineData(HandAlert.LevelYellow, "Helpline alert")]
	[InlineData(HandAlert.LevelBlue, "Helpline alert")]
	[InlineData(HandAlert.LevelRed, "Urgent helpline alert")]
	[InlineData("RED", "Urgent helpline alert")]
	public void LockScreenTitle_KeepsUrgencyAndNothingElse(string level, string expected)
	{
		var alert = Alerts.New(level: level);

		Assert.Equal(expected, alert.LockScreenTitle);
	}

	/// <summary>
	/// An alert with nothing in it at all still says something a responder
	/// can act on. A blank lock-screen notification reads as a fault.
	/// </summary>
	[Fact]
	public void LockScreenText_IsNeverEmpty()
	{
		var alert = new HandAlert();

		Assert.NotEmpty(alert.LockScreenTitle);
		Assert.NotEmpty(HandAlert.LockScreenBody);
	}
}
