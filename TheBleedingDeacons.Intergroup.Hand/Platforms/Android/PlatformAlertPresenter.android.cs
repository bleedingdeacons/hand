using Android.App;
using Android.Content;
using Android.Media;
using Android.OS;
using AndroidX.Core.App;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using AndroidApp = Android.App.Application;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Android half of <see cref="PlatformAlertPresenter"/>: the part that
/// makes a closed handset ring.
/// </summary>
public sealed partial class PlatformAlertPresenter
{
	/// <summary>
	/// Notification ids are ints and alert ids are longs, so the id is
	/// folded into an int. Collisions would only mean one alert's
	/// notification replacing another's, and the in-app list is the record
	/// that matters, but keeping them distinct is free.
	/// </summary>
	private static int NotificationId(long alertId) => unchecked((int)alertId);

	private partial async Task<bool> PlatformRequestPermissionsAsync()
	{
		// Android 13 (API 33) made notifications a runtime permission. A
		// responder who declines it has a handset that cannot alert them
		// while the app is closed, so the answer is surfaced rather than
		// swallowed — the sign-in flow tells them plainly.
		if (!OperatingSystem.IsAndroidVersionAtLeast(33))
		{
			return true;
		}

		// Marshalled to the main thread, and marshalled *here* rather than
		// left to the caller. MAUI throws PermissionException("Permission
		// request must be invoked on main thread") otherwise, because the
		// platform call raises a system dialog against the current activity.
		// Everything upstream is async service code on the thread pool, and
		// an await with ConfigureAwait(false) anywhere in that chain is
		// enough to land here off-thread — so requiring callers to remember
		// would be a trap that only fires on Android 13+, only on a first
		// sign-in, and only on a real device.
		return await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			var status = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
			if (status != PermissionStatus.Granted)
			{
				status = await Permissions.RequestAsync<Permissions.PostNotifications>();
			}

			return status == PermissionStatus.Granted;
		}).ConfigureAwait(false);
	}

	private partial Task PlatformPresentAsync(HandAlert alert)
	{
		var context = AndroidApp.Context;
		EnsureChannel(context);

		// Tapping the notification opens the app on the alert list.
		var intent = new Intent(context, typeof(MainActivity));
		intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
		intent.PutExtra("alert_id", alert.Id);

		var contentIntent = PendingIntent.GetActivity(
			context,
			NotificationId(alert.Id),
			intent,
			PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

		// Built statement by statement rather than as one fluent chain: every
		// NotificationCompat.Builder setter is bound as returning a nullable
		// builder, so a chain is a run of possible null dereferences that
		// says nothing useful. The builder never actually returns null — it
		// returns itself.
		var builder = new NotificationCompat.Builder(context, ChannelId);
		builder.SetContentTitle(alert.Title);
		builder.SetContentText(alert.Body);
		builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(alert.Body));
		builder.SetSmallIcon(Resource.Drawable.ic_hand_alert);

		// Not auto-cancelled and marked ongoing: this is not a message to be
		// swiped away and forgotten. It clears when the alert is acknowledged.
		builder.SetAutoCancel(false);
		builder.SetOngoing(true);
		builder.SetCategory(NotificationCompat.CategoryAlarm);
		builder.SetPriority(NotificationCompat.PriorityMax);
		builder.SetVisibility(NotificationCompat.VisibilityPublic);
		builder.SetContentIntent(contentIntent);

		// The full-screen intent is what turns a notification into an
		// incoming call: the system launches the activity over the lock
		// screen instead of showing a heads-up banner. It is the difference
		// between a responder noticing at 3am and not.
		//
		// Android 14 (API 34) restricted this to apps whose core function is
		// calling or alarms, granted by default for those and revocable by
		// the user. If it has been revoked the system quietly degrades this
		// to a heads-up notification — which still sounds, so there is
		// nothing to handle here beyond asking for it.
		builder.SetFullScreenIntent(contentIntent, highPriority: true);

		try
		{
			NotificationManagerCompat.From(context)?.Notify(NotificationId(alert.Id), builder.Build());
		}
		catch (Exception ex)
		{
			// Thrown when POST_NOTIFICATIONS was declined. The in-app alarm
			// still sounds if the app is running; there is nothing more to do
			// from here.
			Log.Warning(ex, "Notification for alert {AlertId} could not be posted", alert.Id);
		}

		return Task.CompletedTask;
	}

	private partial Task PlatformDismissAsync(long alertId)
	{
		try
		{
			NotificationManagerCompat.From(AndroidApp.Context)?.Cancel(NotificationId(alertId));
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Notification for alert {AlertId} could not be cancelled", alertId);
		}

		return Task.CompletedTask;
	}

	/// <summary>
	/// Create the alert channel if it is not there.
	///
	/// <para><b>The sound is fixed at creation and cannot be changed
	/// afterwards.</b> Android ignores every subsequent attempt to set it
	/// on an existing channel — by design, so an app cannot override a
	/// user's choice. Changing the alarm sound therefore means shipping a
	/// new channel id, not editing this one; editing it looks like it
	/// works and silently does nothing on every device that already has
	/// the app.</para>
	///
	/// <para>Usage is <c>Alarm</c>, matching the in-app player, so the
	/// sound routes to the alarm stream and is not silenced by the ringer
	/// being down.</para>
	/// </summary>
	internal static void EnsureChannel(Context context)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			return;
		}

		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		if (manager is null || manager.GetNotificationChannel(ChannelId) is not null)
		{
			return;
		}

		var channel = new NotificationChannel(
			ChannelId,
			"Helpline alerts",
			NotificationImportance.High)
		{
			Description = "Alerts for the telephone-responder rota. These are meant to wake you.",
			LockscreenVisibility = NotificationVisibility.Public,
		};

		channel.EnableVibration(true);
		channel.EnableLights(true);
		channel.SetBypassDnd(true);

		var soundUri = Android.Net.Uri.Parse(
			$"{ContentResolver.SchemeAndroidResource}://{context.PackageName}/{Resource.Raw.reach_alert}");

		if (soundUri is not null)
		{
			channel.SetSound(
				soundUri,
				new AudioAttributes.Builder()!
					.SetUsage(AudioUsageKind.Alarm)!
					.SetContentType(AudioContentType.Sonification)!
					.Build());
		}
		else
		{
			// The channel is still created — a default-toned alert beats no
			// alert — but this is worth knowing about, because it is the
			// difference between an alarm and a notification chime.
			Log.Error("The alert sound could not be attached to the notification channel");
		}

		manager.CreateNotificationChannel(channel);
	}
}
