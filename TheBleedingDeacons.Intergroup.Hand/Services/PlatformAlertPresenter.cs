using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Raises the operating system's own notification for an alert.
///
/// <para>The shared half is only a shape; everything real is per
/// platform, because "make the handset behave like an incoming call" has
/// no cross-platform expression at all.</para>
/// </summary>
public sealed partial class PlatformAlertPresenter : IPlatformAlertPresenter
{
	/// <summary>
	/// Notification channel id on Android. Must match the
	/// <c>android_channel_id</c> Reach sends
	/// (<c>FcmTransport::ANDROID_CHANNEL</c>) or alerts land on the default
	/// channel with the default sound and none of the alarm behaviour.
	/// </summary>
	public const string ChannelId = "reach_alerts";

	/// <summary>
	/// Notification channel id on Android for anything that is
	/// information rather than an emergency — the notice saying another
	/// responder has already answered.
	///
	/// <para>A separate channel and not a quieter notification on the
	/// alert one, because an Android channel's importance and sound are
	/// fixed when it is created and cannot be changed afterwards. That is
	/// also why there are three of these rather than one reconfigured
	/// three ways, and why a level added later has to be a new id.</para>
	///
	/// <para>Reach sends a matching channel id alongside the level, but
	/// the app does not take instruction from it: it derives the channel
	/// from <see cref="HandAlert.LevelOrDerived"/>, which is the only
	/// thing that still works when talking to a Reach that predates the
	/// level.</para>
	/// </summary>
	public const string NoticeChannelId = "reach_notices";

	/// <summary>
	/// Notification channel id on Android for the middle rung: audible
	/// and heads-up, but it does not take the screen over. See
	/// <see cref="NoticeChannelId"/> on why the levels are separate
	/// channels.
	/// </summary>
	public const string WarningChannelId = "reach_warnings";

	/// <summary>
	/// The silent twins of the three channels above, used in meeting
	/// mode.
	///
	/// <para><b>Twins rather than a setting, because Android leaves no
	/// choice.</b> A channel's sound and importance are fixed when it is
	/// created and an app cannot redefine them afterwards — every
	/// EnsureChannel method here early-returns once its channel exists.
	/// So "the same alert, quieter" has to be a different channel.</para>
	///
	/// <para>Identical to their loud counterparts in every other respect:
	/// same importance, so a red alert still takes the screen over and a
	/// yellow one still shows a heads-up banner; same vibration; same Do
	/// Not Disturb bypass. Only the sound is gone.</para>
	///
	/// <para>These ids are Hand's alone. Reach knows nothing about them —
	/// it sends the loud channel id alongside the level, and the app has
	/// always derived the channel itself rather than taking instruction.
	/// Meeting mode is a property of the handset, not of the alert.</para>
	/// </summary>
	public const string QuietChannelId = "reach_alerts_quiet";

	/// <inheritdoc cref="QuietChannelId"/>
	public const string QuietWarningChannelId = "reach_warnings_quiet";

	/// <inheritdoc cref="QuietChannelId"/>
	public const string QuietNoticeChannelId = "reach_notices_quiet";

	public Task<bool> RequestPermissionsAsync() => PlatformRequestPermissionsAsync();

	public Task PresentAsync(HandAlert alert, bool silent = false)
	{
		ArgumentNullException.ThrowIfNull(alert);

		return PlatformPresentAsync(alert, silent);
	}

	public Task DismissAsync(long alertId) => PlatformDismissAsync(alertId);

	private partial Task<bool> PlatformRequestPermissionsAsync();

	private partial Task PlatformPresentAsync(HandAlert alert, bool silent);

	private partial Task PlatformDismissAsync(long alertId);
}
