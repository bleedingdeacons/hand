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

			// 2. The channels' own settings, which beat the global one.
			if (EveryChannelHidesItsContent(context))
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
	/// Whether every channel an alert can arrive on hides its own
	/// content.
	///
	/// <para><b>Every channel, and the most revealing one decides.</b>
	/// Alerts arrive on one of three channels depending on their level,
	/// and a responder can set each independently. Asking only the alert
	/// channel — which is what this did while there was only one — would
	/// report a handset as safe while its yellow alerts were legible to
	/// anyone standing next to it. The report exists to tell an
	/// intergroup what is readable, so a single channel that shows its
	/// content is the answer.</para>
	///
	/// <para>A channel that has not been created yet, or expresses no
	/// preference, is not evidence of hiding — it falls through to the
	/// global setting, which is where an untouched handset is decided.</para>
	/// </summary>
	private static bool EveryChannelHidesItsContent(Context context)
	{
		string[] channels =
		[
			PlatformAlertPresenter.ChannelId,
			PlatformAlertPresenter.WarningChannelId,
			PlatformAlertPresenter.NoticeChannelId,
		];

		var hiding = 0;

		foreach (var channelId in channels)
		{
			var visibility = ChannelVisibility(context, channelId);

			if (visibility is null)
			{
				// No preference expressed. Not a hiding channel, and not
				// grounds to call the handset unsafe either.
				continue;
			}

			if (visibility is NotificationVisibility.Private or NotificationVisibility.Secret)
			{
				hiding++;
				continue;
			}

			// One channel showing its content is enough to answer.
			return false;
		}

		return hiding > 0;
	}

	/// <summary>
	/// What one channel says about its own lock-screen visibility, or
	/// null when it says nothing.
	///
	/// <para>Android returns <c>VISIBILITY_NO_OVERRIDE</c> (-1000) for a
	/// channel whose owner has expressed no preference, which is the
	/// ordinary case and must not be read as a preference for showing.</para>
	/// </summary>
	private static NotificationVisibility? ChannelVisibility(Context context, string channelId)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			// No notification channels before Oreo, so there is no
			// per-channel setting to consult and the global one decides.
			return null;
		}

		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		var channel = manager?.GetNotificationChannel(channelId);

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
