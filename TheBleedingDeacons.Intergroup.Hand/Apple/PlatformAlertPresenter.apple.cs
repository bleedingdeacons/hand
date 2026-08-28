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

				// The alarm tone for red alone. Yellow and blue take the
				// system's own: the siren is what this app uses to wake
				// people at three in the morning, and a level that may be
				// missed has by definition not earned it.
				Sound = alert.IsUrgent
					? UNNotificationSound.GetSound("reach_alert.wav")
					: UNNotificationSound.Default,
			};

			// <b>iOS has no full-screen intent, so the interruption level
			// is the whole of the ladder here.</b> Three levels, three
			// answers:
			//
			//   red    — time-sensitive: breaks through a Focus mode,
			//            which is the point of the level.
			//   yellow — left alone, so it behaves as an ordinary
			//            notification: it arrives, it sounds, and a
			//            responder who has silenced their phone stays
			//            silenced. That is what "can be missed" means.
			//   blue   — passive: it does not even light the screen, and
			//            is found in the tray when the phone is picked up.
			//
			// Critical, which also beats the ringer switch, is set by
			// Reach on the push payload where the site has Apple's
			// entitlement. It is not ours to choose here.
			if (OperatingSystem.IsIOSVersionAtLeast(15))
			{
				// TimeSensitive2 is the renamed binding for the same
				// underlying value; the original spelling is deprecated.
				if (alert.IsUrgent)
				{
					content.InterruptionLevel = UNNotificationInterruptionLevel.TimeSensitive2;
				}
				else if (alert.IsQuiet)
				{
					content.InterruptionLevel = UNNotificationInterruptionLevel.Passive;
				}
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
