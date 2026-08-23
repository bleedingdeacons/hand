using Android.App;
using Android.Content;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using AndroidApp = Android.App.Application;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Android half of <see cref="LockScreenPrivacy"/>.
///
/// <para><b>Three things decide this, and the app controls none of
/// them.</b> Android's own order of precedence, which this mirrors:</para>
///
/// <list type="number">
///   <item>Whether notifications appear on the lock screen at all
///   (<c>lock_screen_show_notifications</c>). Off means nothing is
///   readable there, which for our purposes is the safe answer — the
///   full-screen intent still rings the phone.</item>
///   <item>Whether the alert channel itself has been set to hide its
///   content. A responder can do this per channel, and it beats the
///   global setting below.</item>
///   <item>Whether sensitive content is shown globally
///   (<c>lock_screen_allow_private_notifications</c>). This is the one
///   that usually decides it, and the one that is commonly left on.</item>
/// </list>
///
/// <para><b>Read every time rather than cached.</b> It is a setting its
/// owner can change while the app is running, and the value is only sent
/// at launch as it is — caching would make a stale answer staler.</para>
///
/// <para>Any failure is <see cref="LockScreenPrivacyState.Unknown"/>, not
/// a guess in either direction. Reporting "hidden" because a read threw
/// would put a reassurance on an intergroup's devices screen that nothing
/// had checked.</para>
/// </summary>
public sealed partial class LockScreenPrivacy
{
	/// <summary>
	/// Secure-settings keys. Named as string literals because the
	/// constants for them are hidden API on Android and not surfaced by
	/// the binding; reading a Secure setting by name needs no permission,
	/// though writing one does.
	/// </summary>
	private const string ShowNotificationsKey = "lock_screen_show_notifications";
	private const string AllowPrivateKey = "lock_screen_allow_private_notifications";

	private partial string PlatformState()
	{
		try
		{
			var context = AndroidApp.Context;
			var resolver = context.ContentResolver;

			if (resolver is null)
			{
				return LockScreenPrivacyState.Unknown;
			}

			// 1. Nothing on the lock screen at all.
			if (Android.Provider.Settings.Secure.GetInt(resolver, ShowNotificationsKey, 1) == 0)
			{
				return LockScreenPrivacyState.Hidden;
			}

			// 2. The channel's own setting, which beats the global one.
			var channelVisibility = AlertChannelVisibility(context);
			if (channelVisibility is NotificationVisibility.Private or NotificationVisibility.Secret)
			{
				return LockScreenPrivacyState.Hidden;
			}

			// 3. The global "show all notification content".
			return Android.Provider.Settings.Secure.GetInt(resolver, AllowPrivateKey, 1) == 0
				? LockScreenPrivacyState.Hidden
				: LockScreenPrivacyState.Shown;
		}
		catch (Exception ex)
		{
			// Never a guess. See the class docblock.
			Log.Warning(ex, "Lock-screen notification privacy could not be read");
			return LockScreenPrivacyState.Unknown;
		}
	}

	/// <summary>
	/// What the alert channel says about its own lock-screen visibility,
	/// or null when it says nothing.
	///
	/// <para>Android returns <c>VISIBILITY_NO_OVERRIDE</c> (-1000) for a
	/// channel whose owner has expressed no preference, which is the
	/// ordinary case and must not be read as a preference for showing.</para>
	/// </summary>
	private static NotificationVisibility? AlertChannelVisibility(Context context)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			// No notification channels before Oreo, so there is no
			// per-channel setting to consult and the global one decides.
			return null;
		}

		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		var channel = manager?.GetNotificationChannel(PlatformAlertPresenter.ChannelId);

		if (channel is null)
		{
			return null;
		}

		var visibility = channel.LockscreenVisibility;

		// -1000 is VISIBILITY_NO_OVERRIDE. The binding types the property
		// as NotificationVisibility, which has no member for it, so the
		// comparison is against the raw value.
		return (int)visibility == -1000 ? null : visibility;
	}
}
