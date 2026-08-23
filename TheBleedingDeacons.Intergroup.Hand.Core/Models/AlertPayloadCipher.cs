using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// Opens a pushed alert.
///
/// <para>Reach encrypts the <b>whole</b> data payload to a secret this
/// handset was given once, at enrolment, and sends the result as a
/// single <c>ciphertext</c> field. Not the readable half of it — all of
/// it, including the id, the kind, the priority and whatever extras the
/// raising plugin attached. Nothing else travels alongside.</para>
///
/// <para>The point is that Google carries ciphertext and nothing else. A
/// push crosses Firebase's servers and lands in a notification history;
/// an alert is supposed to carry no personal data, but that is a
/// convention the server enforces by capping and stripping rather than
/// by reading meaning, and encrypting the lot removes the question
/// instead of policing it.</para>
///
/// <para>AES-256-GCM over gzip, with the envelope Reach packs: 12 bytes
/// of nonce, then the 16-byte tag, then the ciphertext, all base64. GCM
/// authenticates, so a payload altered in transit fails to open rather
/// than decrypting to something plausible. The gzip is not for
/// tidiness — sealing and base64'ing the largest payload the server will
/// accept overflows FCM's 4KB limit without it.</para>
/// </summary>
public static class AlertPayloadCipher
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int KeyBytes = 32;

	/// <summary>
	/// The payload the ciphertext carries, or null when it cannot be
	/// opened.
	///
	/// <para>Null covers every reason at once — no key, a key that does
	/// not fit, a truncated payload, a tampered one, something that
	/// decompresses to a shape this does not recognise — because the
	/// caller can do nothing different about any of them. What it does
	/// about all of them is ignore the push and tell the server this
	/// handset is broken; see <see cref="HandAlert.FromPushData"/>.</para>
	/// </summary>
	public static Dictionary<string, string>? Open(string ciphertext, string base64Key)
	{
		if (string.IsNullOrEmpty(ciphertext) || string.IsNullOrEmpty(base64Key))
		{
			return null;
		}

		if (!TryDecodeBase64(base64Key, out var key) || key.Length != KeyBytes)
		{
			return null;
		}

		if (!TryDecodeBase64(ciphertext, out var packed) || packed.Length <= NonceBytes + TagBytes)
		{
			return null;
		}

		var nonce = packed.AsSpan(0, NonceBytes);
		var tag = packed.AsSpan(NonceBytes, TagBytes);
		var body = packed.AsSpan(NonceBytes + TagBytes);
		var compressed = new byte[body.Length];

		try
		{
			using var gcm = new AesGcm(key, TagBytes);
			gcm.Decrypt(nonce, body, tag, compressed);
		}
		catch (CryptographicException)
		{
			// The tag did not verify: a wrong key, or a payload that was
			// altered on the way. Both mean the same thing here.
			return null;
		}

		var json = Inflate(compressed);
		if (json is null)
		{
			return null;
		}

		try
		{
			var opened = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

			// Ordinal throughout, matching the map FCM hands the platform
			// and the one HandAlert reads from. A case-insensitive lookup
			// here would let a plugin extra named "Kind" shadow the alert's
			// own, which is the collision the server's own merge order
			// exists to prevent.
			return opened is null ? null : new Dictionary<string, string>(opened, StringComparer.Ordinal);
		}
		catch (JsonException)
		{
			// Decrypted and decompressed to something that is not a
			// string→string map. Only reachable if the two ends disagree
			// about the format, which is worth failing softly rather than
			// throwing into a push handler.
			return null;
		}
	}

	/// <summary>
	/// Undo the server's <c>gzencode</c>, or null if it will not undo.
	/// </summary>
	private static byte[]? Inflate(byte[] compressed)
	{
		try
		{
			using var source = new MemoryStream(compressed, writable: false);
			using var gzip = new GZipStream(source, CompressionMode.Decompress);
			using var inflated = new MemoryStream();

			gzip.CopyTo(inflated);

			return inflated.ToArray();
		}
		catch (InvalidDataException)
		{
			// Decrypted cleanly but is not gzip. The tag verified, so this
			// is the two ends disagreeing about the format rather than
			// anything an attacker did.
			return null;
		}
	}

	private static bool TryDecodeBase64(string value, out byte[] bytes)
	{
		var buffer = new byte[((value.Length * 3) + 3) / 4];

		if (Convert.TryFromBase64String(value, buffer, out var written))
		{
			bytes = buffer[..written];
			return true;
		}

		bytes = [];
		return false;
	}
}
