using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// Opens the encrypted half of an alert.
///
/// <para>Reach encrypts an alert's title, body and reference to a secret
/// this handset was given once, at enrolment, and sends the result as a
/// single <c>ciphertext</c> field. Everything else in the push stays
/// readable, because this app needs it before it can open anything —
/// which alert to acknowledge, whether this is the removal notice that
/// must never alarm, how urgent it is, and when it expires.</para>
///
/// <para>The point is that Google carries ciphertext. A push crosses
/// Firebase's servers and lands in a notification history; a caller's
/// situation has no business in either.</para>
///
/// <para>AES-256-GCM, with the envelope Reach packs: 12 bytes of nonce,
/// then the 16-byte tag, then the ciphertext, all base64. GCM
/// authenticates, so a payload altered in transit fails to open rather
/// than decrypting to something plausible.</para>
/// </summary>
public static class AlertPayloadCipher
{
	private const int NonceBytes = 12;
	private const int TagBytes = 16;
	private const int KeyBytes = 32;

	/// <summary>
	/// The three fields the ciphertext carries, or null when it cannot be
	/// opened.
	///
	/// <para>Null covers every reason at once — no key, a key that does
	/// not fit, a truncated payload, a tampered one — because the caller
	/// can do nothing different about any of them. What it does about all
	/// of them is ring anyway; see the handler.</para>
	/// </summary>
	public static AlertText? Open(string ciphertext, string base64Key)
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
		var plaintext = new byte[body.Length];

		try
		{
			using var gcm = new AesGcm(key, TagBytes);
			gcm.Decrypt(nonce, body, tag, plaintext);
		}
		catch (CryptographicException)
		{
			// The tag did not verify: a wrong key, or a payload that was
			// altered on the way. Both mean the same thing here.
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<AlertText>(Encoding.UTF8.GetString(plaintext));
		}
		catch (JsonException)
		{
			// Decrypted to something that is not the shape expected. Only
			// reachable if the two ends disagree about the format, which is
			// worth failing softly rather than throwing into a push handler.
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
