using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// Opening the encrypted half of an alert.
///
/// <para>These tests seal with the same construction Reach uses —
/// AES-256-GCM, nonce then tag then ciphertext, base64 — rather than
/// calling the app's own code to produce their input. A test that
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

	/// <summary>Seal the way Reach's PayloadCipher does.</summary>
	private static (string Ciphertext, string Key) Seal(
		string title = Title,
		string body = Body,
		string reference = Reference)
	{
		var key = RandomNumberGenerator.GetBytes(32);
		var nonce = RandomNumberGenerator.GetBytes(12);

		var json = JsonSerializer.Serialize(new AlertText
		{
			Title = title,
			Body = body,
			Reference = reference,
		});

		var plaintext = Encoding.UTF8.GetBytes(json);
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using var gcm = new AesGcm(key, 16);
		gcm.Encrypt(nonce, plaintext, ciphertext, tag);

		return (Convert.ToBase64String([.. nonce, .. tag, .. ciphertext]), Convert.ToBase64String(key));
	}

	[Fact]
	public void Open_RecoversWhatReachSealed()
	{
		var (ciphertext, key) = Seal();

		var opened = AlertPayloadCipher.Open(ciphertext, key);

		Assert.NotNull(opened);
		Assert.Equal(Title, opened.Title);
		Assert.Equal(Body, opened.Body);
		Assert.Equal(Reference, opened.Reference);
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

	[Fact]
	public void Open_HandlesTextThatIsNotAscii()
	{
		// Titles come from whatever a plugin or an administrator typed.
		var (ciphertext, key) = Seal(title: "Rendez-vous — café, 3 o’clock");

		var opened = AlertPayloadCipher.Open(ciphertext, key);

		Assert.NotNull(opened);
		Assert.Equal("Rendez-vous — café, 3 o’clock", opened.Title);
	}
}
