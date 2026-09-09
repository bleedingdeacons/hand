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
///
/// <para><b>Meeting mode is a mute, not a downgrade.</b> This head has
/// no silent twin channels to post to the way Android does and no
/// interruption level to leave alone the way iOS does; what it has is
/// <c>MuteAudio</c>, which silences the toast and changes nothing else.
/// The scenario, the text and the persistence are all untouched, so a
/// silenced red alert still sits on screen until a responder deals with
/// it — which on this head is the only thing left, because the in-app
/// alarm is muted by the same flag and there is no vibration motor to
/// fall back on.</para>
/// </summary>
public sealed partial class PlatformAlertPresenter
{
	private partial Task<bool> PlatformRequestPermissionsAsync()
	{
		// Windows has no runtime notification permission; delivery is
		// governed by Focus Assist and per-app settings the user owns.
		return Task.FromResult(true);
	}

	private partial Task PlatformPresentAsync(HandAlert alert, bool silent)
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
				// The scenario is set either way. Meeting mode takes the
				// sound and nothing else — the same rule the other two
				// heads follow, where a silenced red alert keeps its
				// Android importance and full-screen intent and its iOS
				// interruption level. Here that means the toast still
				// stays on screen until it is dismissed.
				builder.SetScenario(AppNotificationScenario.Alarm);

				if (!silent)
				{
					// S1075 reads ms-appx:/// as a hardcoded absolute URI.
					// It is not one: it is the packaged app's own resource
					// scheme, resolved by Windows against this app's
					// content, and the wav is a MauiAsset that lands under
					// Resources/Raw. There is nothing to make configurable.
					// AlertAlarm.windows.cs names the same file the same
					// way. Narrow suppression, so a real absolute path
					// added here still gets caught.
#pragma warning disable S1075
					builder.SetAudioUri(
						new Uri("ms-appx:///Resources/Raw/reach_alert.wav"),
						AppNotificationAudioLooping.Loop);
#pragma warning restore S1075
				}
			}

			// <b>Muting is a positive act, not the absence of SetAudioUri.</b>
			// An Alarm-scenario toast plays Windows' own looping alarm
			// sound when the notification names none, and an ordinary toast
			// plays the default notification sound — so simply declining to
			// set audio above would leave a "silent" alert making noise,
			// which is the exact failure meeting mode exists to prevent.
			//
			// It matters more here than on the mobile heads. AlertService
			// reads meeting mode once and passes it to both this and the
			// in-app alarm so a handset is never silent in the tray and
			// audible in the room; on Windows the alarm is the whole
			// audible story and vibration is unsupported, so once both are
			// quiet this toast is the only signal a responder gets.
			if (silent)
			{
				builder.MuteAudio();
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
