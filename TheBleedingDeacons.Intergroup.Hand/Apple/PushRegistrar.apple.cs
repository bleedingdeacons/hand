using Serilog;
using UIKit;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Apple half of <see cref="PushRegistrar"/>.
///
/// <para>Unlike Android, the token is not something we can ask for
/// synchronously: iOS delivers it to
/// <c>AppDelegate.RegisteredForRemoteNotifications</c> some time after
/// registration is requested. The delegate stashes it in
/// <see cref="DeviceToken"/>, and this waits briefly for it to appear.</para>
///
/// <para>The value is the APNs device token. Reach sends through FCM,
/// which maps APNs tokens to its own registration tokens when the
/// Firebase iOS SDK is present; a build without that SDK reports no
/// token and polls instead, which is a degraded handset rather than a
/// broken one.</para>
/// </summary>
public sealed partial class PushRegistrar
{
	/// <summary>
	/// Set by the app delegate when Apple hands the token over. Static
	/// because the delegate is created by the platform, not by DI.
	/// </summary>
	public static string DeviceToken { get; set; } = string.Empty;

	private partial string PlatformProvider() => Fcm;

	private async partial Task<string> PlatformGetTokenAsync()
	{
		if (!string.IsNullOrEmpty(DeviceToken))
		{
			return DeviceToken;
		}

		try
		{
			await MainThread.InvokeOnMainThreadAsync(
				UIApplication.SharedApplication.RegisterForRemoteNotifications).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Remote notification registration could not be started");
			return string.Empty;
		}

		// Registration is a round trip to Apple. Wait a short while for the
		// delegate to be called rather than failing enrolment outright — but
		// not indefinitely, because a handset with no network would then
		// never finish signing in. Enrolling without a token is fine; the
		// app re-registers the token as soon as it arrives.
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		while (string.IsNullOrEmpty(DeviceToken) && DateTimeOffset.UtcNow < deadline)
		{
			await Task.Delay(200).ConfigureAwait(false);
		}

		return DeviceToken;
	}
}
