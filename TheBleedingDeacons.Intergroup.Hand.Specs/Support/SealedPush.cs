using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Support;

/// <summary>
/// The server side of a push, so a scenario can send one.
///
/// <para><b>It seals by construction rather than by calling Hand's own
/// code.</b> Gzip, then AES-256-GCM, nonce then tag then ciphertext, all
/// base64 — written out here the way Reach's <c>PayloadCipher</c> writes
/// it. Sealing with the code under test would pass just as happily if
/// both ends of the format changed together, and that is the one failure
/// this has to catch: the two halves live in different repositories and
/// ship on different days.</para>
/// </summary>
public static class SealedPush
{
	/// <summary>A key of the size Reach issues at enrolment.</summary>
	public static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

	/// <summary>
	/// The whole data map, as Reach seals it: everything about the alert,
	/// including its id, inside the one blob.
	/// </summary>
	public static Dictionary<string, string> Payload() =>
		new(StringComparer.Ordinal)
		{
			["alert_id"] = "12",
			["message_uuid"] = "message-12",
			["kind"] = "call_request",
			["source"] = "reach",
			["priority"] = "urgent",
			["level"] = "red",
			["response"] = "first",
			["title"] = "Callback wanted CR-000123",
			["body"] = "Male 12th-stepper wanted in BS5",
			["reference"] = "CR-000123",
			["created_at"] = "1757620800",
			["expires_at"] = "0",
			["channel"] = "reach_alerts",
			["sound"] = "reach_alert",
		};

	/// <summary>The push as it comes off FCM: one field, and it is sealed.</summary>
	public static Dictionary<string, string> Seal(IDictionary<string, string> payload, string base64Key) =>
		new(StringComparer.Ordinal) { ["ciphertext"] = Ciphertext(payload, base64Key) };

	public static string Ciphertext(IDictionary<string, string> payload, string base64Key)
	{
		var key = Convert.FromBase64String(base64Key);
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
}
