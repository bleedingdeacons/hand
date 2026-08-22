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
	/// Seal the readable half the way Reach does, so a test can send the
	/// shape a handset actually receives.
	/// </summary>
	private static string Seal(string title, string body, string reference)
	{
		var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
		var plaintext = System.Text.Encoding.UTF8.GetBytes(
			System.Text.Json.JsonSerializer.Serialize(new AlertText
			{
				Title = title,
				Body = body,
				Reference = reference,
			}));
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using var gcm = new System.Security.Cryptography.AesGcm(TestKey, 16);
		gcm.Encrypt(nonce, plaintext, ciphertext, tag);

		return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
	}

	[Fact]
	public void FromPushData_ReadsEveryField()
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "4242",
			["kind"] = "shift_uncovered",
			["source"] = "trusted",
			["priority"] = "urgent",
			["ciphertext"] = Seal("Shift uncovered", "Nobody is on the helpline.", "SHIFT-2026-08-15-N"),
			["created_at"] = "1755250000",
			["expires_at"] = "1755253600",
			["has_contact"] = "1",
		}, TestKeyBase64);

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
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = rawId,
			["title"] = "Shift uncovered",
		});

		Assert.Null(alert);
	}

	[Fact]
	public void FromPushData_RefusesAMessageWithNoIdAtAll() =>
		Assert.Null(HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)));

	[Fact]
	public void FromPushData_RejectsNull() =>
		Assert.Throws<ArgumentNullException>(() => HandAlert.FromPushData(null!));

	[Fact]
	public void FromPushData_DefaultsEveryFieldItWasNotSent()
	{
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
		});

		Assert.NotNull(alert);
		Assert.Equal(string.Empty, alert.Kind);
		// Nothing sealed, so the readable half is the fault message rather
		// than the empty strings this used to leave behind.
		Assert.True(alert.IsUnreadable);
		Assert.Equal(HandAlert.UnsealedMessage, alert.Title);
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
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
			["expires_at"] = "soon",
		});

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
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
			["has_contact"] = raw,
		});

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
		var alert = HandAlert.FromPushData(new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["alert_id"] = "7",
			["title"] = "Shift uncovered",
			["channel"] = "reach_alerts",
			["sound"] = "reach_alert",
			["rota_slot"] = "night",
			["region"] = "bristol",
		});

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
	/// A push whose readable half is encrypted. This is the shape Reach
	/// sends to any Android handset that holds a payload key.
	/// </summary>
	[Fact]
	public void FromPushData_OpensAnEncryptedPayload()
	{
		var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
		var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
		var plaintext = System.Text.Encoding.UTF8.GetBytes(
			"""{"title":"Callback wanted CR-9","body":"Wanted in BS5","reference":"CR-9"}""");
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using (var gcm = new System.Security.Cryptography.AesGcm(key, 16))
		{
			gcm.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "9",
				["kind"] = "call_request",
				["ciphertext"] = Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]),
			},
			Convert.ToBase64String(key));

		Assert.NotNull(alert);
		Assert.Equal("Callback wanted CR-9", alert.Title);
		Assert.Equal("Wanted in BS5", alert.Body);
		Assert.Equal("CR-9", alert.Reference);

		// The sealed blob must not survive as a payload entry, or the
		// ciphertext would be shown wherever extras are displayed.
		Assert.False(alert.Payload.ContainsKey("ciphertext"));
	}

	/// <summary>
	/// A handset that cannot open the payload still knows an alert exists,
	/// what kind it is and when it expires — so it can still ring — and
	/// says plainly what is wrong instead of showing a blank alert.
	/// </summary>
	[Fact]
	public void FromPushData_StillYieldsAnAlertWhenThePayloadWillNotOpen()
	{
		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "9",
				["kind"] = "call_request",
				["priority"] = "urgent",
				["ciphertext"] = Convert.ToBase64String(
					System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)),
			},
			Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));

		Assert.NotNull(alert);
		Assert.Equal(9, alert.Id);
		Assert.Equal("call_request", alert.Kind);
		Assert.True(alert.IsUrgent);
		Assert.True(alert.IsUnreadable);
		Assert.Equal(HandAlert.UnopenableMessage, alert.Title);
		Assert.Contains("Sign in again", alert.Body, StringComparison.Ordinal);
	}

	/// <summary>
	/// An alert that arrived with nothing sealed in it is a fault, not a
	/// fallback: it means the server does not know this handset's key.
	///
	/// <para>Showing the plaintext would hide that, and hide it
	/// permanently — everything would keep working while the text this
	/// whole feature exists to protect crossed Google in the clear.</para>
	/// </summary>
	[Fact]
	public void FromPushData_TreatsAnUnsealedAlertAsAFault()
	{
		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "9",
				["kind"] = "call_request",
				["title"] = "Callback wanted for Joanne",
				["body"] = "Wanted in BS5",
			});

		Assert.NotNull(alert);
		Assert.Equal(9, alert.Id);
		Assert.True(alert.IsUnreadable);
		Assert.Equal(HandAlert.UnsealedMessage, alert.Title);
		Assert.DoesNotContain("Joanne", alert.Title, StringComparison.Ordinal);
		Assert.DoesNotContain("Joanne", alert.Body, StringComparison.Ordinal);
	}

	/// <summary>A sealed alert that opens is not flagged as a fault.</summary>
	[Fact]
	public void FromPushData_DoesNotFlagAnAlertThatOpened()
	{
		var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
		var nonce = System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
		var plaintext = System.Text.Encoding.UTF8.GetBytes(
			"""{"title":"Callback wanted","body":"BS5","reference":"CR-1"}""");
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using (var gcm = new System.Security.Cryptography.AesGcm(key, 16))
		{
			gcm.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		var alert = HandAlert.FromPushData(
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["alert_id"] = "9",
				["ciphertext"] = Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]),
			},
			Convert.ToBase64String(key));

		Assert.NotNull(alert);
		Assert.False(alert.IsUnreadable);
		Assert.Equal("Callback wanted", alert.Title);
	}

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

	/// <summary>
	/// Urgency survives redaction deliberately: it is not a secret, and it
	/// is what tells a responder whether the phone can wait.
	/// </summary>
	[Theory]
	[InlineData("normal", "Helpline alert")]
	[InlineData("urgent", "Urgent helpline alert")]
	[InlineData("URGENT", "Urgent helpline alert")]
	public void LockScreenTitle_KeepsUrgencyAndNothingElse(string priority, string expected)
	{
		var alert = Alerts.New();
		alert.Priority = priority;

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
