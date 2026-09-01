using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Windows half of <see cref="PlatformAlertPresenter"/>.
///
/// <para>The toast is the visual half only. Windows has no push, so the
/// alert always arrives through the poll — which means Hand is running,
/// which means the in-app alarm is what actually makes the noise. The
/// toast's own looping audio is set anyway so a responder who has the
/// app minimised gets both.</para>
///
/// <para><c>SetScenario(Alarm)</c> is the important call: it keeps the
/// toast on screen until it is dismissed rather than fading after a few
/// seconds, which is the behaviour a duty alert needs.</para>
/// </summary>
public sealed partial class PlatformAlertPresenter
{
	private partial Task<bool> PlatformRequestPermissionsAsync()
	{
		// Windows has no runtime notification permission; delivery is
		// governed by Focus Assist and per-app settings the user owns.
		return Task.FromResult(true);
	}

	private partial Task PlatformPresentAsync(HandAlert alert)
	{
		try
		{
			var builder = new AppNotificationBuilder()
				.AddText(alert.Title)
				.AddText(alert.Body)
				.SetTag(alert.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));

			// Red alone gets the alarm treatment. Both settings below
			// exist to make a duty alert impossible to miss — an
			// Alarm-scenario toast stays on screen until it is dismissed
			// and the looping audio does not stop by itself — and neither
			// is appropriate for a level that is meant to be missable, or
			// for news that somebody else has already dealt with it.
			//
			// Yellow and blue both fall through to an ordinary toast that
			// fades, with the system's own sound. Windows has no third
			// rung between "a toast" and "an alarm that will not stop", so
			// the two quieter levels look the same here; they are still
			// told apart by their card colour in the app.
			if (alert.IsUrgent)
			{
				builder
					.SetScenario(AppNotificationScenario.Alarm)
					.SetAudioUri(
						new Uri("ms-appx:///Resources/Raw/reach_alert.wav"),
						AppNotificationAudioLooping.Loop);
			}

			AppNotificationManager.Default.Show(builder.BuildNotification());
		}
		catch (Exception ex)
		{
			// Unpackaged builds cannot show app notifications at all. The
			// in-app alarm still sounds and the window still shows the alert,
			// so this is a degraded presentation rather than a lost alert.
			Log.Debug(ex, "Toast for alert {AlertId} could not be shown", alert.Id);
		}

		return Task.CompletedTask;
	}

	private partial async Task PlatformDismissAsync(long alertId)
	{
		try
		{
			await AppNotificationManager.Default
				.RemoveByTagAsync(alertId.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.AsTask()
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Toast for alert {AlertId} could not be removed", alertId);
		}
	}
}
