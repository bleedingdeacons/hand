using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// The Apple half, shared by both Apple heads.
///
/// <para>Reads the settings rather than calling
/// <c>RequestAuthorizationAsync</c>, which would raise the system prompt
/// — see the interface on why an indicator must never do that.
/// <see cref="PlatformAlertPresenter"/> is where the asking happens.</para>
///
/// <para><b>Provisional counts as granted.</b> It is the state an app is
/// in when iOS has let it deliver quietly without asking anybody, and a
/// handset in it does receive alerts — they land in the notification
/// centre instead of on the lock screen. Reporting that as "off" would
/// send a responder to a settings screen to fix something that works.
/// </para>
/// </summary>
public sealed partial class NotificationPermission
{
	private async partial Task<bool> PlatformIsGrantedAsync()
	{
		var settings = await UNUserNotificationCenter.Current
			.GetNotificationSettingsAsync().ConfigureAwait(false);

		return settings.AuthorizationStatus
			is UNAuthorizationStatus.Authorized
			or UNAuthorizationStatus.Provisional
			or UNAuthorizationStatus.Ephemeral;
	}
}
