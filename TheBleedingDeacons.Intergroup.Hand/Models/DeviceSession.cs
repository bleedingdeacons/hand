using System.Text.Json.Serialization;

namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// What Reach says about this handset when it is asked.
///
/// <para>Returned both by enrolment (with <see cref="Token"/> populated,
/// the only time the plaintext token is ever sent) and by the session
/// check at launch (without it).</para>
/// </summary>
public class DeviceSession
{
	/// <summary>
	/// The bearer token. Present only in an enrolment response; Reach
	/// cannot reissue it, so it goes straight to secure storage.
	/// </summary>
	[JsonPropertyName("token")]
	public string Token { get; set; } = string.Empty;

	[JsonPropertyName("device_id")]
	public long DeviceId { get; set; }

	/// <summary>
	/// The responder's anonymous name, for showing who this handset is
	/// signed in as. Deliberately the anonymous name and not an email —
	/// it is displayed on a screen that sits face-up on a table.
	/// </summary>
	[JsonPropertyName("responder")]
	public string Responder { get; set; } = string.Empty;

	[JsonPropertyName("platform")]
	public string Platform { get; set; } = string.Empty;

	[JsonPropertyName("push_provider")]
	public string PushProvider { get; set; } = string.Empty;

	[JsonPropertyName("label")]
	public string Label { get; set; } = string.Empty;

	[JsonPropertyName("authorised")]
	public bool Authorised { get; set; }
}

/// <summary>
/// The outcome of an attempt to sign in or check the session.
///
/// <para>A result type rather than exceptions because every one of these
/// outcomes is ordinary: a responder mistypes a password, a certification
/// lapses, a handset is out of signal. Only the last of those is worth a
/// retry, and the caller needs to be able to tell them apart to say
/// anything useful.</para>
/// </summary>
public sealed class ReachResult<T>
{
	private ReachResult(bool success, T? value, ReachFailure failure, string message)
	{
		Success = success;
		Value = value;
		Failure = failure;
		Message = message;
	}

	public bool Success { get; }

	public T? Value { get; }

	public ReachFailure Failure { get; }

	/// <summary>Reach's own message, suitable for showing to a responder.</summary>
	public string Message { get; }

	public static ReachResult<T> Ok(T value) =>
		new(true, value, ReachFailure.None, string.Empty);

	public static ReachResult<T> Fail(ReachFailure failure, string message) =>
		new(false, default, failure, message);
}

public enum ReachFailure
{
	None = 0,

	/// <summary>Could not reach the server at all. Worth retrying.</summary>
	Network,

	/// <summary>Reach is not configured, or its address is wrong.</summary>
	NotConfigured,

	/// <summary>
	/// The token is gone — revoked, expired, or never valid. The app must
	/// drop it and show sign-in.
	/// </summary>
	Unauthenticated,

	/// <summary>
	/// Proven identity, refused access: not a telephone responder, or not
	/// certified. Showing this plainly is what tells a responder to go and
	/// sort their certification out.
	/// </summary>
	NotEligible,

	/// <summary>Wrong email or password.</summary>
	InvalidCredentials,

	/// <summary>Too many attempts; wait and try again.</summary>
	RateLimited,

	/// <summary>Anything else the server said no to.</summary>
	Server,
}
