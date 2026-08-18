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
/// <para><b>Push is currently disabled on the Apple heads, deliberately.</b>
/// What the delegate receives is an <i>APNs device token</i>, but Reach
/// sends through FCM and <c>message.token</c> requires an <i>FCM
/// registration token</i>. They are different identifiers; FCM rejects
/// an APNs token outright. Producing an FCM token needs the Firebase
/// iOS SDK in the app, which is not referenced yet.</para>
///
/// <para>So <see cref="PlatformProvider"/> reports no transport rather
/// than claiming FCM. Reporting FCM would be worse than reporting
/// nothing: the handset would enrol looking push-capable, Reach's admin
/// list would show it as "Push", the dispatcher would spend a send on it
/// for every alert, and every one of those would fail — while the
/// responder had been told their phone would ring with the app closed.
/// Poll-only is a degraded handset; a handset that lies about being
/// push-capable is a broken promise.</para>
///
/// <para>To enable: add <c>Xamarin.Firebase.iOS.CloudMessaging</c>,
/// configure Firebase in <c>AppDelegate</c>, return <c>Fcm</c> from
/// <see cref="PlatformProvider"/>, and read
/// <c>Messaging.SharedInstance.FcmToken</c> below instead of
/// <see cref="DeviceToken"/>. The registration plumbing here already
/// works and is what supplies APNs the token Firebase needs.</para>
/// </summary>
public sealed partial class PushRegistrar
{
	/// <summary>
	/// Set by the app delegate when Apple hands the token over. Static
	/// because the delegate is created by the platform, not by DI.
	/// </summary>
	public static string DeviceToken { get; set; } = string.Empty;

	// Empty means "no push transport, poll instead". See the class
	// docblock: an APNs token is not an FCM token, so claiming FCM here
	// would enrol a handset that can never be pushed to. Return Fcm once
	// the Firebase iOS SDK is supplying a real registration token.
	private partial string PlatformProvider() => string.Empty;

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
