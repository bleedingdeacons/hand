using Foundation;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using UserNotifications;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Apple half of <see cref="PlatformAlertPresenter"/>.
///
/// <para>This only posts notifications for alerts that arrived while Hand
/// was running. When the app is closed the system has already displayed
/// the APNs payload before any of our code could run — that path is
/// entirely Reach's payload and Apple's, and is why the sound is named in
/// the push rather than chosen here.</para>
/// </summary>
public sealed partial class PlatformAlertPresenter
{
	private partial async Task<bool> PlatformRequestPermissionsAsync()
	{
		try
		{
			// Sound is requested explicitly: without it iOS shows the alert
			// silently, which for this app is the same as not showing it.
			//
			// CriticalAlert is deliberately NOT requested here. It needs
			// Apple's entitlement in the provisioning profile, and asking for
			// it without one gets the whole authorisation request refused —
			// taking ordinary notifications down with it.
			var (granted, error) = await UNUserNotificationCenter.Current
				.RequestAuthorizationAsync(
					UNAuthorizationOptions.Alert
					| UNAuthorizationOptions.Sound
					| UNAuthorizationOptions.Badge)
				.ConfigureAwait(false);

			if (error is not null)
			{
				Log.Warning("Notification authorisation failed: {Error}", error.LocalizedDescription);
			}

			return granted;
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Notification authorisation could not be requested");
			return false;
		}
	}

	private partial async Task PlatformPresentAsync(HandAlert alert)
	{
		try
		{
			var content = new UNMutableNotificationContent
			{
				Title = alert.Title,
				Body = alert.Body,
				Sound = UNNotificationSound.GetSound("reach_alert.wav"),
			};

			if (OperatingSystem.IsIOSVersionAtLeast(15))
			{
				// Time-sensitive breaks through a Focus mode without needing
				// any entitlement. Critical (which also beats the ringer
				// switch) is set by Reach on the push payload when the site
				// has Apple's entitlement — it is not ours to choose here.
				//
				// TimeSensitive2 is the renamed binding for the same
				// underlying value; the original spelling is deprecated.
				content.InterruptionLevel = UNNotificationInterruptionLevel.TimeSensitive2;
			}

			var request = UNNotificationRequest.FromIdentifier(
				alert.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
				content,
				trigger: null);

			await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Notification for alert {AlertId} could not be posted", alert.Id);
		}
	}

	private partial Task PlatformDismissAsync(long alertId)
	{
		try
		{
			var id = alertId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			UNUserNotificationCenter.Current.RemoveDeliveredNotifications([id]);
			UNUserNotificationCenter.Current.RemovePendingNotificationRequests([id]);
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Notification for alert {AlertId} could not be withdrawn", alertId);
		}

		return Task.CompletedTask;
	}
}
