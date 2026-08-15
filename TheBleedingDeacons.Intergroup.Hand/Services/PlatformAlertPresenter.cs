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

	public Task<bool> RequestPermissionsAsync() => PlatformRequestPermissionsAsync();

	public Task PresentAsync(HandAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		return PlatformPresentAsync(alert);
	}

	public Task DismissAsync(long alertId) => PlatformDismissAsync(alertId);

	private partial Task<bool> PlatformRequestPermissionsAsync();

	private partial Task PlatformPresentAsync(HandAlert alert);

	private partial Task PlatformDismissAsync(long alertId);
}
