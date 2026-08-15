using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Signing this handset in and out, and knowing who it belongs to.
/// </summary>
public interface IDeviceAuthService
{
	/// <summary>
	/// The signed-in responder, or null when this handset is not enrolled.
	/// </summary>
	DeviceSession? Current { get; }

	bool IsSignedIn { get; }

	/// <summary>
	/// Check a stored token against Reach at launch. Returns false when
	/// there is no token, or when the one held is no longer accepted — in
	/// which case it has already been cleared.
	/// </summary>
	Task<bool> RestoreAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Sign in through an identity provider, using the system browser.
	/// </summary>
	Task<ReachResult<DeviceSession>> SignInWithSsoAsync(string provider, CancellationToken cancellationToken = default);

	/// <summary>Sign in with an email and password, no browser involved.</summary>
	Task<ReachResult<DeviceSession>> SignInWithPasswordAsync(
		string email, string password, CancellationToken cancellationToken = default);

	/// <summary>Revoke this handset and forget its token.</summary>
	Task SignOutAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Send Reach a rotated push registration token. Called when the
	/// platform hands over a new one, which Firebase does without warning
	/// and which is the usual reason a handset stops ringing.
	/// </summary>
	Task RegisterPushTokenAsync(string pushToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Obtains this platform's push registration token, if it has one.
/// </summary>
public interface IPushRegistrar
{
	/// <summary>
	/// The transport this platform uses — <c>fcm</c> on Android and iOS,
	/// empty on Windows and macOS, which have no FCM coverage and poll.
	/// </summary>
	string Provider { get; }

	/// <summary>
	/// The current registration token, or empty if there is none. Empty is
	/// a normal answer, not a failure: it means this handset collects its
	/// own alerts.
	/// </summary>
	Task<string> GetTokenAsync();
}
