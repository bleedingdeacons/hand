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
		var granted = await RequestNotificationPermissionAsync().ConfigureAwait(false);

		// Asked second, and separately, because the two are not the same
		// kind of thing: notifications are a hard requirement and their
		// answer is the return value, while this one is best-effort and a
		// refusal leaves a handset that still works, just less reliably.
		RequestBatteryExemption();

		return granted;
	}

	private static async Task<bool> RequestNotificationPermissionAsync()
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

	/// <summary>
	/// Ask to be exempted from battery optimisation.
	/// </summary>
	/// <remarks>
	/// <para><b>This is what keeps a closed handset reachable.</b> Android
	/// sorts apps into App Standby buckets by how often they are opened,
	/// and a duty phone is by design almost never opened — so Hand drifts
	/// down to RARE, where the number of high-priority FCM messages
	/// allowed to wake it is capped. Past that cap the system does not
	/// deliver and drop; it <i>defers</i>, holding the message for the
	/// next maintenance window, which can be hours. The alert is not lost,
	/// it is late, and a helpline alert that arrives an hour late is lost
	/// in every sense that matters.</para>
	///
	/// <para>An exempted app is not bucketed, so the cap does not apply.
	/// That is the whole reason REQUEST_IGNORE_BATTERY_OPTIMIZATIONS is in
	/// the manifest. It was declared there from the start and never
	/// actually requested, which meant a debug build worked — a debugger
	/// pins the app to the ACTIVE bucket — and a real handset on someone's
	/// bedside table did not.</para>
	///
	/// <para>Best-effort throughout. The system dialog can be declined,
	/// and on a device with no activity to handle the intent it does not
	/// appear at all. Both leave a handset that still polls while open and
	/// still receives whatever push the bucket allows, so nothing here is
	/// worth failing sign-in over — hence no return value and a warning
	/// rather than a throw.</para>
	/// </remarks>
	private static void RequestBatteryExemption()
	{
		try
		{
			var context = AndroidApp.Context;
			var packageName = context.PackageName;
			if (string.IsNullOrEmpty(packageName))
			{
				return;
			}

			// Already exempt: asking again would put a dialog in front of a
			// responder for something they have already agreed to.
			var power = (PowerManager?)context.GetSystemService(Context.PowerService);
			if (power is null || power.IsIgnoringBatteryOptimizations(packageName))
			{
				return;
			}

			var intent = new Intent(Android.Provider.Settings.ActionRequestIgnoreBatteryOptimizations);
			intent.SetData(Android.Net.Uri.Parse($"package:{packageName}"));

			// NewTask because this is started from the application context,
			// not an activity — the sign-in flow runs it off the UI thread.
			intent.AddFlags(ActivityFlags.NewTask);

			context.StartActivity(intent);

			Log.Information("Asked to be exempted from battery optimisation");
		}
		catch (Exception ex)
		{
			// ActivityNotFoundException on a device with the settings screen
			// removed, or a SecurityException if the permission is ever
			// stripped from the manifest. Neither stops the app working.
			Log.Warning(ex, "Battery optimisation exemption could not be requested");
		}
	}

	private partial Task PlatformPresentAsync(HandAlert alert, bool silent)
	{
		// Three levels, three channels, three notifications. Only red
		// falls through to the alarm treatment below — the other two are
		// notifications a responder can miss, which is the difference the
		// level exists to express.
		//
		// Meeting mode doubles that to six: each level has a silent twin,
		// because Android fixes a channel's sound when it is created and
		// will not let it be changed. Everything else about the
		// notification is identical — see QuietChannelId.
		if (alert.IsQuiet)
		{
			return PresentQuietly(alert, silent);
		}

		if (alert.IsWarning)
		{
			return PresentWarning(alert, silent);
		}

		var context = AndroidApp.Context;
		EnsureChannel(context, silent);
		var channelId = silent ? QuietChannelId : ChannelId;

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
		var builder = new NotificationCompat.Builder(context, channelId);
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
		builder.SetContentIntent(contentIntent);

		// Private, with a redacted stand-in for the lock screen. Without the
		// public version Android would substitute its own "Contents hidden"
		// line, which says less than we can and looks like a fault.
		//
		// Two things this does not change. The full-screen intent still
		// fires, so the phone still rings like an incoming call — visibility
		// governs what is legible, not whether the handset alarms. And it
		// only takes effect on a *secure* lock screen: on a handset with no
		// PIN or biometric there is nothing to redact behind, and Android
		// shows the real notification. That is the user's decision to have
		// made, and not one the app can override.
		//
		// The same is true of a secure lock screen whose owner has chosen
		// to show all notification content, which is the commoner case by
		// far and the one this used to leave unsaid. Android then ignores
		// the public version below and puts the alert's own words in front
		// of whoever is standing there. Setting this is still worth doing —
		// it is the whole of what an app may do, and it works for everyone
		// who has chosen to hide sensitive content — but it is an offer
		// rather than a guarantee, and the difference is what
		// LockScreenPrivacy exists to report.
		builder.SetVisibility(NotificationCompat.VisibilityPrivate);
		builder.SetPublicVersion(RedactedVersion(context, alert, contentIntent, channelId));

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

	/// <summary>
	/// Post something that is information rather than an emergency.
	///
	/// <para><b>A separate channel, because a channel's importance and
	/// sound are fixed when it is created.</b> Android ignores every
	/// later attempt to change them — deliberately, so an app cannot
	/// override a user's choice — so a quiet notification posted to the
	/// alarm channel is not quiet at all: it arrives at maximum
	/// importance with the looping alarm tone, which is the entire thing
	/// this is avoiding. There is no way to do it on one channel.</para>
	///
	/// <para>Auto-cancelled and not ongoing, unlike an alert: this is a
	/// message to be read and swiped away, and there is nothing here that
	/// has to stay in the tray until it is dealt with.</para>
	///
	/// <para>Still redacted on the lock screen. A notice quotes the
	/// original message's own title so it says which message it is about,
	/// which means it carries the same freehand text the alert did and
	/// deserves the same treatment.</para>
	/// </summary>
	private static Task PresentQuietly(HandAlert alert, bool silent)
	{
		EnsureNoticeChannel(AndroidApp.Context, silent);

		return PresentWithoutTakingOver(
			alert,
			silent ? QuietNoticeChannelId : NoticeChannelId,
			NotificationCompat.CategoryStatus);
	}

	/// <summary>
	/// Post the middle rung: it sounds and shows a heads-up banner, and
	/// then behaves like a message.
	///
	/// <para>The same notification as a notice — auto-cancelled, not
	/// ongoing, no full-screen intent — on a channel whose importance is
	/// High rather than Default. That one difference is the whole of
	/// yellow: it gets attention, and it does not demand the screen or
	/// keep ringing. See <see cref="EnsureWarningChannel"/>.</para>
	///
	/// <para>The category is Message rather than Status because a yellow
	/// alert is addressed to the responder, where a notice reports on
	/// something. It is what Android uses to rank and group.</para>
	/// </summary>
	private static Task PresentWarning(HandAlert alert, bool silent)
	{
		EnsureWarningChannel(AndroidApp.Context, silent);

		return PresentWithoutTakingOver(
			alert,
			silent ? QuietWarningChannelId : WarningChannelId,
			NotificationCompat.CategoryMessage);
	}

	/// <summary>
	/// The notification both non-alarm levels post, differing only in
	/// their channel and category.
	///
	/// <para>Shared rather than written twice because the parts that must
	/// not drift are the ones neither level varies: the redacted public
	/// version, the content intent, and the absence of a full-screen
	/// intent. A copy would be a second place for the lock-screen
	/// treatment to be forgotten.</para>
	/// </summary>
	private static Task PresentWithoutTakingOver(HandAlert alert, string channelId, string category)
	{
		var context = AndroidApp.Context;

		var intent = new Intent(context, typeof(MainActivity));
		intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
		intent.PutExtra("alert_id", alert.Id);

		var contentIntent = PendingIntent.GetActivity(
			context,
			NotificationId(alert.Id),
			intent,
			PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

		var builder = new NotificationCompat.Builder(context, channelId);
		builder.SetContentTitle(alert.Title);
		builder.SetContentText(alert.Body);
		builder.SetStyle(new NotificationCompat.BigTextStyle().BigText(alert.Body));
		builder.SetSmallIcon(Resource.Drawable.ic_hand_alert);
		builder.SetAutoCancel(true);
		builder.SetOngoing(false);
		builder.SetCategory(category);
		builder.SetPriority(
			category == NotificationCompat.CategoryMessage
				? NotificationCompat.PriorityHigh
				: NotificationCompat.PriorityDefault);
		builder.SetContentIntent(contentIntent);
		builder.SetVisibility(NotificationCompat.VisibilityPrivate);
		builder.SetPublicVersion(RedactedVersion(context, alert, contentIntent, channelId));

		try
		{
			NotificationManagerCompat.From(context)?.Notify(NotificationId(alert.Id), builder.Build());
		}
		catch (Exception ex)
		{
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
	/// <summary>
	/// The notification a secure lock screen shows instead of the real one.
	///
	/// <para>Built here rather than reusing the builder above because the
	/// two have almost nothing in common: this one carries no payload text,
	/// no big-text style and no full-screen intent — it is a placard, not an
	/// alarm. It keeps the icon, the channel and the content intent so that
	/// it looks like the same notification and tapping it still opens the
	/// app.</para>
	///
	/// <para>The wording comes from <see cref="HandAlert"/> so that what a
	/// stranger may read is decided in the half of the app that has tests,
	/// rather than inline in a platform file that CI only compiles.</para>
	/// </summary>
	/// <remarks>
	/// Returns a nullable because <c>Build()</c> is bound as one, in the same
	/// way every setter on the builder is — see the note above on why this
	/// file does not pretend otherwise. <c>SetPublicVersion</c> accepts null
	/// and treats it as "no public version", which degrades to Android's own
	/// "Contents hidden" line rather than to an unredacted notification.
	/// </remarks>
	private static Notification? RedactedVersion(
		Context context,
		HandAlert alert,
		PendingIntent? contentIntent,
		string channelId)
	{
		var builder = new NotificationCompat.Builder(context, channelId);
		builder.SetContentTitle(alert.LockScreenTitle);
		builder.SetContentText(HandAlert.LockScreenBody);
		builder.SetSmallIcon(Resource.Drawable.ic_hand_alert);
		builder.SetCategory(NotificationCompat.CategoryAlarm);
		builder.SetVisibility(NotificationCompat.VisibilityPublic);
		builder.SetContentIntent(contentIntent);

		return builder.Build();
	}

	/// <summary>
	/// Create the notices channel if it is not there.
	///
	/// <para>Default importance and the system's own notification sound:
	/// it should appear and be readable, not demand anything. Vibration,
	/// lights and the DND bypass are all deliberately absent — every one
	/// of them exists on the alert channel to wake somebody, and nothing
	/// on this channel is worth waking anybody for.</para>
	/// </summary>
	internal static void EnsureNoticeChannel(Context context, bool silent = false)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			return;
		}

		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		var id = silent ? QuietNoticeChannelId : NoticeChannelId;
		if (manager is null || manager.GetNotificationChannel(id) is not null)
		{
			return;
		}

		var channel = new NotificationChannel(
			id,
			silent ? "Helpline updates (in a meeting)" : "Helpline updates",
			NotificationImportance.Default)
		{
			Description = "News about alerts somebody else has already answered. These will not wake you.",
			LockscreenVisibility = NotificationVisibility.Private,
		};

		// A Default-importance channel still chimes unless told not to.
		if (silent)
		{
			channel.SetSound(null, null);
		}

		manager.CreateNotificationChannel(channel);
	}

	/// <summary>
	/// The middle rung's channel: it makes a noise and shows a heads-up
	/// banner, and stops there.
	///
	/// <para>High importance, so it appears over whatever is on screen
	/// and sounds — that is what separates it from a notice. But the
	/// alert channel's siren, its alarm audio usage and its Do Not
	/// Disturb bypass are all absent: those exist to wake somebody who is
	/// asleep, and yellow is explicitly the level that may be missed and
	/// caught up with. The notification itself supplies the rest of the
	/// difference — no full-screen intent, and it can be swiped
	/// away.</para>
	/// </summary>
	internal static void EnsureWarningChannel(Context context, bool silent = false)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			return;
		}

		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		var id = silent ? QuietWarningChannelId : WarningChannelId;
		if (manager is null || manager.GetNotificationChannel(id) is not null)
		{
			return;
		}

		var channel = new NotificationChannel(
			id,
			silent ? "Helpline messages (in a meeting)" : "Helpline messages",
			NotificationImportance.High)
		{
			Description = "Alerts that should get your attention but will not ring like a call.",
			LockscreenVisibility = NotificationVisibility.Private,
		};

		channel.EnableVibration(true);

		// Meeting mode: the banner and the buzz stay, the tone goes.
		if (silent)
		{
			channel.SetSound(null, null);
		}

		manager.CreateNotificationChannel(channel);
	}

	internal static void EnsureChannel(Context context, bool silent = false)
	{
		if (!OperatingSystem.IsAndroidVersionAtLeast(26))
		{
			return;
		}

		var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
		var id = silent ? QuietChannelId : ChannelId;
		if (manager is null || manager.GetNotificationChannel(id) is not null)
		{
			return;
		}

		var channel = new NotificationChannel(
			id,
			silent ? "Helpline alerts (in a meeting)" : "Helpline alerts",
			NotificationImportance.High)
		{
			Description = "Alerts for the telephone-responder rota. These are meant to wake you.",

			// The channel default, which matters only on a fresh install:
			// the guard above returns early when the channel already exists,
			// and Android does not let an app redefine one afterwards —
			// visibility becomes the user's setting from that point on.
			//
			// So this is not what delivers the redaction. Each notification
			// sets its own visibility, and where the two disagree the system
			// takes the more private of them, which is why handsets that
			// created this channel under an earlier build are still covered.
			LockscreenVisibility = NotificationVisibility.Private,
		};

		channel.EnableVibration(true);
		channel.EnableLights(true);
		channel.SetBypassDnd(true);

		// <b>Meeting mode stops here, and keeps everything else.</b>
		// Importance stays High so a red alert still takes the screen over
		// with its full-screen intent, vibration and lights stay on, and
		// the Do Not Disturb bypass above is untouched — a responder who
		// silenced the room did not ask to stop being alerted. Only the
		// siren is absent.
		if (silent)
		{
			channel.SetSound(null, null);
			manager.CreateNotificationChannel(channel);
			return;
		}

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
