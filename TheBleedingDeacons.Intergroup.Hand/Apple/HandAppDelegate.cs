using Foundation;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services;
using UIKit;

namespace TheBleedingDeacons.Intergroup.Hand;

/// <summary>
/// The push plumbing both Apple heads share.
/// </summary>
/// <remarks>
/// <para>Here rather than duplicated into the two <c>AppDelegate</c> files
/// because iOS and Mac Catalyst want exactly the same thing, and two copies
/// of an APNs registration callback is two places for one to be forgotten.
/// The heads keep their own <c>AppDelegate</c> — it carries the
/// <c>[Register("AppDelegate")]</c> the platform looks for by name, and
/// <c>Program.Main</c> names that type — and inherit this.</para>
///
/// <para><b>The callbacks below are the reason iOS push works at all.</b>
/// <c>RegisteredForRemoteNotifications</c> is where Apple hands over the
/// APNs device token, and handing it to Firebase is what lets Firebase mint
/// the FCM registration token Reach actually sends to. Miss it and
/// enrolment produces no token, silently, on a build that otherwise looks
/// configured.</para>
/// </remarks>
public abstract class HandAppDelegate : MauiUIApplicationDelegate
{
	// launchOptions is nullable on the base — iOS passes null for a launch
	// that was not prompted by anything. Matching it keeps CS8765 quiet.
	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// Base first: it builds the MAUI app, and Firebase's own logging
		// wants Serilog already standing.
		var launched = base.FinishedLaunching(application, launchOptions);

		// Never throws — a build with no Firebase configuration reports
		// unavailable and the handset enrols poll-only.
		FirebasePush.Configure();

		return launched;
	}

	/// <summary>
	/// Apple has issued this installation an APNs device token.
	/// </summary>
	/// <remarks>
	/// <para>Handed straight to Firebase, which cannot produce an FCM
	/// registration token until it has one. This is the round trip
	/// <c>FirebasePush.TokenAsync</c> waits on during enrolment.</para>
	///
	/// <para><b>Exported rather than overridden, and it has to be.</b>
	/// <c>MauiUIApplicationDelegate</c> derives from <c>UIResponder</c> and
	/// <i>implements</i> <c>IUIApplicationDelegate</c> — it does not inherit
	/// the <c>UIApplicationDelegate</c> class where these are virtual, so
	/// there is nothing to override and the compiler says so (CS0115). What
	/// makes iOS call this is the selector below matching the one Apple
	/// invokes; the C# name is ours. Renaming the method is safe, changing
	/// the string is not.</para>
	/// </remarks>
	[Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
	public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken)
	{
		FirebasePush.SetApnsToken(deviceToken);
	}

	/// <summary>
	/// Apple refused to issue one.
	/// </summary>
	/// <remarks>
	/// Logged rather than surfaced. The usual causes are a build signed
	/// without the push entitlement, a simulator, or no network — all of
	/// which leave a handset that still collects its alerts on the poll,
	/// which is the documented degraded state rather than a failure to
	/// report to the responder mid-enrolment.
	/// </remarks>
	[Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
	public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error)
	{
		Log.Warning(
			"Apple refused to register this handset for remote notifications ({Error}); it will poll only",
			error?.LocalizedDescription ?? "no reason given");
	}
}
