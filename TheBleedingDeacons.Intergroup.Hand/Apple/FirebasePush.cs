using Firebase.CloudMessaging;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using UIKit;
using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Firebase Cloud Messaging on the Apple heads.
/// </summary>
/// <remarks>
/// <para><b>Why a binding is needed at all.</b> iOS hands the app an
/// <i>APNs device token</i>. Reach sends through FCM, and
/// <c>message.token</c> requires an <i>FCM registration token</i> — a
/// different identifier, which FCM rejects if given the wrong one. Firebase
/// is the thing that exchanges one for the other: the APNs token goes in at
/// <see cref="Messaging.ApnsToken"/> and the registration token comes out.
/// That exchange is the entire reason this file exists.</para>
///
/// <para><b>Absence is a documented state, not a failure.</b> Without
/// <c>GoogleService-Info.plist</c> in the bundle there is no Firebase
/// project to register against, and this reports unavailable rather than
/// throwing — exactly as the Android head does without
/// <c>google-services.json</c>. A handset in that state enrols poll-only:
/// alerts arrive on the poll interval while the app is running, and a
/// closed app does not ring. Said out loud in the log, because the
/// alternative is discovering it on a duty handset at three in the
/// morning.</para>
///
/// <para><b>Critical alerts are not requested here.</b> Hand's alarm is
/// loud by design, but <c>UNAuthorizationOptions.CriticalAlert</c> needs an
/// entitlement Apple grants by application, and asking for one that has not
/// been granted fails the whole authorisation request rather than degrading
/// it. So this asks for alert, badge and sound. Worth revisiting if the
/// intergroup ever holds a paid team that can apply for it.</para>
/// </remarks>
internal static class FirebasePush
{
	/// <summary>
	/// The bundle resource Firebase reads its project configuration from.
	/// Named here rather than left to Firebase so its absence can be
	/// detected before <c>App.Configure()</c> is called.
	/// </summary>
	private const string ConfigResource = "GoogleService-Info";

	private static readonly TokenWatcher Watcher = new();

	/// <summary>
	/// Set once iOS has handed over the APNs token and it has been given to
	/// Firebase. Until then there is nothing to exchange and a token fetch
	/// would fail.
	/// </summary>
	private static bool _apnsTokenSet;

	/// <summary>
	/// Whether this build has a Firebase project behind it and configured
	/// cleanly. False means poll-only, and <c>PushRegistrar</c> reports no
	/// transport rather than claiming FCM — see its remarks for why a
	/// handset that lies about being push-capable is worse than one that
	/// admits it is not.
	/// </summary>
	public static bool Available { get; private set; }

	/// <summary>
	/// Start Firebase, if this build has the configuration for it. Called
	/// from the app delegate at launch, and safe to call more than once.
	/// </summary>
	public static void Configure()
	{
		if (Available)
		{
			return;
		}

		// Checked rather than caught. Firebase raises an Objective-C
		// exception for a missing plist, and while .NET marshals those,
		// relying on an exception to discover a supported configuration is
		// how a launch crash gets shipped.
		if (NSBundle.MainBundle.PathForResource(ConfigResource, "plist") is null)
		{
			Log.Warning(
				"No {Resource}.plist in the bundle, so there is no Firebase project to register with. "
				+ "This handset will enrol POLL-ONLY: alerts arrive on the poll interval while Hand is "
				+ "open, and a closed app will not ring.",
				ConfigResource);

			return;
		}

		try
		{
			Firebase.Core.App.Configure();
			Messaging.SharedInstance.Delegate = Watcher;
			Available = true;

			Log.Information("Firebase configured; this handset can be pushed to");
		}
#pragma warning disable CA1031 // Deliberately broad: nothing here is worth a launch crash.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			// A malformed or wrong-project plist. The handset still works,
			// on the poll, which is the same degraded state as no plist at
			// all and is reported the same way.
			Log.Error(ex, "Firebase could not be configured; this handset will poll only");
		}
	}

	/// <summary>
	/// Hand Firebase the APNs token iOS has just issued. Called from the app
	/// delegate.
	/// </summary>
	public static void SetApnsToken(NSData deviceToken)
	{
		if (!Available)
		{
			return;
		}

		Messaging.SharedInstance.ApnsToken = deviceToken;
		_apnsTokenSet = true;
	}

	/// <summary>
	/// The FCM registration token for this install, or empty.
	/// </summary>
	/// <remarks>
	/// Three things have to happen in order, and the order is the whole
	/// difficulty: the responder must permit notifications, iOS must issue
	/// an APNs token, and only then can Firebase exchange it. Each step is
	/// bounded, because enrolment is waiting on this and a handset with no
	/// signal must still be able to sign in — it enrols poll-only and
	/// re-registers as soon as a token arrives.
	/// </remarks>
	public static async Task<string> TokenAsync()
	{
		if (!Available)
		{
			return string.Empty;
		}

		try
		{
			var (granted, error) = await UNUserNotificationCenter.Current
				.RequestAuthorizationAsync(
					UNAuthorizationOptions.Alert
					| UNAuthorizationOptions.Badge
					| UNAuthorizationOptions.Sound)
				.ConfigureAwait(false);

			if (error is not null)
			{
				Log.Warning("Notification authorisation failed: {Error}", error.LocalizedDescription);
			}

			if (!granted)
			{
				// Refused, or refused earlier and remembered. Registering
				// anyway would still produce a token, and Reach would then
				// spend a send on every alert for a phone that shows nothing.
				Log.Warning(
					"Notifications are not permitted on this handset, so it will poll only. "
					+ "A responder can allow them in iOS Settings and sign in again.");

				return string.Empty;
			}

			await MainThread.InvokeOnMainThreadAsync(
				UIApplication.SharedApplication.RegisterForRemoteNotifications).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Deliberately broad: enrolment must not fail over this.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "Remote notification registration could not be started");
			return string.Empty;
		}

		// Registration is a round trip to Apple. Wait a short while for the
		// delegate to be called rather than failing enrolment outright — but
		// not indefinitely, because a handset with no network would then
		// never finish signing in.
		var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
		while (!_apnsTokenSet && DateTimeOffset.UtcNow < deadline)
		{
			await Task.Delay(200).ConfigureAwait(false);
		}

		if (!_apnsTokenSet)
		{
			Log.Warning("Apple did not issue an APNs token in time; enrolling poll-only for now");
			return string.Empty;
		}

		try
		{
			// FetchToken rather than reading FcmToken, which is null until
			// the first exchange has happened — and would therefore report
			// no token on the very launch that enrols.
			return await Messaging.SharedInstance.FetchTokenAsync().ConfigureAwait(false) ?? string.Empty;
		}
#pragma warning disable CA1031 // Deliberately broad: see above.
		catch (Exception ex)
#pragma warning restore CA1031
		{
			Log.Warning(ex, "FCM registration token could not be obtained; this handset will poll only");
			return string.Empty;
		}
	}

	/// <summary>
	/// Hears about token rotations.
	/// </summary>
	/// <remarks>
	/// Tokens rotate without warning, and a stale one is the usual reason a
	/// handset silently stops ringing — so a new one is sent on
	/// immediately. This mirrors the Android head's <c>OnNewToken</c>
	/// exactly, including the backstop:
	/// <c>DeviceAuthService.RestoreAsync</c> re-registers the current token
	/// at every launch, so a rotation missed here is corrected the next time
	/// the app opens rather than being lost.
	/// </remarks>
	private sealed class TokenWatcher : MessagingDelegate
	{
		public override void DidReceiveRegistrationToken(Messaging messaging, string? fcmToken)
		{
			if (string.IsNullOrEmpty(fcmToken))
			{
				// Firebase reports null when it has retired a token without
				// yet issuing another. Nothing to send; the next call brings
				// the replacement.
				return;
			}

			Log.Information("Firebase issued a new registration token");

			var auth = IPlatformApplication.Current?.Services.GetService<IDeviceAuthService>();
			if (auth is null)
			{
				// Too early in launch for the container to exist. Registered
				// at the next opportunity by RestoreAsync, which sends the
				// current token unconditionally for this reason.
				return;
			}

			_ = Task.Run(async () =>
			{
				try
				{
					await auth.RegisterPushTokenAsync(fcmToken).ConfigureAwait(false);
				}
#pragma warning disable CA1031 // Deliberately broad: a background rotation must not crash the app.
				catch (Exception ex)
#pragma warning restore CA1031
				{
					Log.Error(ex, "New push token could not be registered with Reach");
				}
			});
		}
	}
}
