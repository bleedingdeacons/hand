using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// Opening a pushed alert.
///
/// <para>These tests seal with the same construction Reach uses — gzip,
/// then AES-256-GCM, nonce then tag then ciphertext, base64 — rather
/// than calling the app's own code to produce their input. A test that
/// encrypted with the code under test would pass just as happily if both
/// ends of the format changed together, which is the one failure this
/// has to catch: the two halves live in different repositories and ship
/// on different days.</para>
/// </summary>
public sealed class AlertPayloadCipherTests
{
	private const string Title = "Callback wanted CR-000123";
	private const string Body = "Male 12th-stepper wanted in BS5";
	private const string Reference = "CR-000123";

	/// <summary>The whole data map, as Reach now seals it.</summary>
	private static Dictionary<string, string> Payload(string title = Title) =>
		new(StringComparer.Ordinal)
		{
			["alert_id"] = "12",
			["kind"] = "call_request",
			["source"] = "reach",
			["priority"] = "normal",
			["title"] = title,
			["body"] = Body,
			["reference"] = Reference,
			["channel"] = "reach_alerts",
			["sound"] = "reach_alert",
		};

	/// <summary>Seal the way Reach's PayloadCipher does.</summary>
	private static (string Ciphertext, string Key) Seal(IDictionary<string, string>? payload = null)
	{
		var key = RandomNumberGenerator.GetBytes(32);

		return (SealWith(key, payload ?? Payload()), Convert.ToBase64String(key));
	}

	private static string SealWith(byte[] key, IDictionary<string, string> payload)
	{
		var nonce = RandomNumberGenerator.GetBytes(12);
		var plaintext = Gzip(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using var gcm = new AesGcm(key, 16);
		gcm.Encrypt(nonce, plaintext, ciphertext, tag);

		return Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]);
	}

	/// <summary>PHP's <c>gzencode</c>: gzip, not raw deflate.</summary>
	private static byte[] Gzip(byte[] raw)
	{
		using var compressed = new MemoryStream();

		using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			gzip.Write(raw, 0, raw.Length);
		}

		return compressed.ToArray();
	}

	[Fact]
	public void Open_RecoversEverythingReachSealed()
	{
		var (ciphertext, key) = Seal();

		var opened = AlertPayloadCipher.Open(ciphertext, key);

		Assert.NotNull(opened);
		Assert.Equal(Title, opened["title"]);
		Assert.Equal(Body, opened["body"]);
		Assert.Equal(Reference, opened["reference"]);

		// The fields that used to travel in the clear are in here now.
		// That is the whole change: nothing readable crosses Google, and
		// nothing is left outside for a future field to be added beside.
		Assert.Equal("12", opened["alert_id"]);
		Assert.Equal("call_request", opened["kind"]);
		Assert.Equal("reach", opened["source"]);
		Assert.Equal("normal", opened["priority"]);
		Assert.Equal("reach_alerts", opened["channel"]);
		Assert.Equal("reach_alert", opened["sound"]);
	}

	/// <summary>
	/// Keys are matched ordinally everywhere else — the server's own merge
	/// order relies on it to stop a plugin's extras shadowing the alert's
	/// own fields — so a case-insensitive lookup here would open a hole
	/// the server closed.
	/// </summary>
	[Fact]
	public void Open_MatchesKeysOrdinally()
	{
		var (ciphertext, key) = Seal();

		var opened = AlertPayloadCipher.Open(ciphertext, key);

		Assert.NotNull(opened);
		Assert.True(opened.ContainsKey("kind"));
		Assert.False(opened.ContainsKey("Kind"));
	}

	[Fact]
	public void Open_RefusesTheWrongKey()
	{
		var (ciphertext, _) = Seal();
		var someoneElses = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

		Assert.Null(AlertPayloadCipher.Open(ciphertext, someoneElses));
	}

	[Fact]
	public void Open_RefusesATamperedPayload()
	{
		// GCM authenticates, so an altered payload must fail to open
		// rather than decrypt to something plausible.
		var (ciphertext, key) = Seal();
		var bytes = Convert.FromBase64String(ciphertext);
		bytes[^1] ^= 0xFF;

		Assert.Null(AlertPayloadCipher.Open(Convert.ToBase64String(bytes), key));
	}

	[Theory]
	[InlineData("", "")]
	[InlineData("not base64 at all!!", "")]
	[InlineData("", "not base64 at all!!")]
	public void Open_TreatsUnusableInputAsUnopenable(string ciphertext, string key)
	{
		Assert.Null(AlertPayloadCipher.Open(ciphertext, key));
	}

	[Fact]
	public void Open_RefusesAKeyOfTheWrongLength()
	{
		var (ciphertext, _) = Seal();
		var tooShort = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

		Assert.Null(AlertPayloadCipher.Open(ciphertext, tooShort));
	}

	[Fact]
	public void Open_RefusesAPayloadTooShortToHoldAnEnvelope()
	{
		var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

		Assert.Null(AlertPayloadCipher.Open(Convert.ToBase64String(RandomNumberGenerator.GetBytes(20)), key));
	}

	/// <summary>
	/// A payload that decrypts cleanly but was never compressed. The tag
	/// verifies, so this is the two ends disagreeing about the format
	/// rather than anything an attacker did — and it must still be null
	/// rather than an exception thrown into a push handler.
	/// </summary>
	[Fact]
	public void Open_RefusesAPayloadThatWasNotCompressed()
	{
		var key = RandomNumberGenerator.GetBytes(32);
		var nonce = RandomNumberGenerator.GetBytes(12);
		var plaintext = Encoding.UTF8.GetBytes("""{"alert_id":"12"}""");
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using (var gcm = new AesGcm(key, 16))
		{
			gcm.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		Assert.Null(AlertPayloadCipher.Open(
			Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]),
			Convert.ToBase64String(key)));
	}

	/// <summary>
	/// Reach's data block is a string→string map. Anything else is the
	/// two ends disagreeing, and must fail softly.
	/// </summary>
	[Fact]
	public void Open_RefusesAPayloadThatIsNotAStringMap()
	{
		var key = RandomNumberGenerator.GetBytes(32);
		var nonce = RandomNumberGenerator.GetBytes(12);
		var plaintext = Gzip(Encoding.UTF8.GetBytes("""{"alert_id":{"nested":true}}"""));
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using (var gcm = new AesGcm(key, 16))
		{
			gcm.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		Assert.Null(AlertPayloadCipher.Open(
			Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]),
			Convert.ToBase64String(key)));
	}

	[Fact]
	public void Open_HandlesTextThatIsNotAscii()
	{
		// Titles come from whatever a plugin or an administrator typed.
		var (ciphertext, key) = Seal(Payload(title: "Rendez-vous — café, 3 o’clock"));

		var opened = AlertPayloadCipher.Open(ciphertext, key);

		Assert.NotNull(opened);
		Assert.Equal("Rendez-vous — café, 3 o’clock", opened["title"]);
	}

	/// <summary>
	/// The worst case Reach's own caps allow — title 200, body 1000,
	/// reference 64, and 2000 bytes of plugin payload — has to fit FCM's
	/// 4KB data message. Sealed and base64'd without compression it does
	/// not, which is why the format gzips first.
	/// </summary>
	[Fact]
	public void Open_HandlesTheLargestPayloadReachWillSend()
	{
		var payload = Payload();
		payload["title"] = new string('t', 200);
		payload["body"] = new string('b', 1000);
		payload["reference"] = new string('r', 64);
		payload["area"] = new string('a', 2000);

		var (ciphertext, key) = Seal(payload);

		Assert.True(
			ciphertext.Length < 4096,
			$"the sealed payload must fit an FCM data message; it was {ciphertext.Length} bytes");

		var opened = AlertPayloadCipher.Open(ciphertext, key);

		Assert.NotNull(opened);
		Assert.Equal(new string('b', 1000), opened["body"]);
		Assert.Equal(new string('a', 2000), opened["area"]);
	}
}
