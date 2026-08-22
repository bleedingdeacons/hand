using Android.App;
using Android.Content;
using Android.OS;
using Firebase.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using AndroidApp = Android.App.Application;

namespace TheBleedingDeacons.Intergroup.Hand.Platforms.Android;

/// <summary>
/// Receives Reach's push messages.
///
/// <para>This runs with no UI and possibly with no app: Android starts
/// the service to deliver a message even when Hand has been swiped away.
/// That is precisely why Reach sends <b>data-only</b> messages. A message
/// carrying a <c>notification</c> block would be handled by the system
/// tray instead and this method would never be called — so Hand could
/// never raise the full-screen intent that makes the handset ring like a
/// call, and a responder would get one polite ding.</para>
///
/// <para>The one thing that defeats this entirely is the app being
/// force-stopped, by the user or by an OEM battery manager: a stopped app
/// receives nothing at all until it is opened again. Short of that, what
/// decides whether a message arrives <i>promptly</i> is the App Standby
/// bucket — see <c>PlatformAlertPresenter.RequestBatteryExemption</c>,
/// which is what keeps Hand out of it.</para>
/// </summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class HandFirebaseMessagingService : FirebaseMessagingService
{
	/// <summary>
	/// How long delivery may take before it is abandoned.
	///
	/// <para>Firebase allows roughly twenty seconds for a high-priority
	/// message before it stops waiting and may kill the process. Ten
	/// leaves room to give up, log it and release the wakelock tidily
	/// inside that budget. Nothing on this path makes a network call —
	/// the alert arrived in the payload — so ten seconds is already an
	/// enormous margin over what it takes.</para>
	/// </summary>
	private static readonly TimeSpan DeliveryBudget = TimeSpan.FromSeconds(10);

	public override void OnMessageReceived(RemoteMessage message)
	{
		ArgumentNullException.ThrowIfNull(message);

		base.OnMessageReceived(message);

		try
		{
			var data = message.Data;
			if (data is null || data.Count == 0)
			{
				Log.Debug("Push message arrived with no data payload; ignoring");
				return;
			}

			// Read from secure storage on the delivery path rather than
			// cached in a field: this service is created and destroyed by
			// Android at will, so there is no instance lifetime to cache
			// against, and a key read once at construction would be a key
			// this handset kept using after signing out.
			//
			// Blocking on it is deliberate. OnMessageReceived has no async
			// form, and returning before the notification is posted is how
			// an alert silently never arrives.
			var payloadKey = HeadlessAlerts.PayloadKey();

			var alert = HandAlert.FromPushData(data, payloadKey);
			if (alert is null)
			{
				// No usable id: the alert could never be acknowledged, so it
				// would ring forever. The poll picks it up properly instead.
				Log.Warning("Push message could not be read as an alert; leaving it to the poll");
				return;
			}

			Deliver(alert);
		}
		catch (Exception ex)
		{
			// Never throw out of a platform callback on the delivery path.
			Log.Error(ex, "Push message could not be processed");
		}
	}

	/// <summary>
	/// Get the alert in front of the responder, and do not return until
	/// that has happened.
	/// </summary>
	/// <remarks>
	/// <para><b>Synchronous on purpose, and this is the whole point of the
	/// method.</b> FirebaseMessagingService holds a wakelock for exactly
	/// as long as <see cref="OnMessageReceived"/> is executing and drops
	/// it the moment that returns. Handing the work to a fire-and-forget
	/// task and returning therefore releases the one thing keeping the
	/// process alive, and a dozing handset is free to freeze it before the
	/// notification is ever posted. It looks fine on a developer's desk,
	/// because a debugger pins the process and the work always finishes.
	/// It is the difference between ringing and not on a phone in a pocket
	/// at 3am.</para>
	///
	/// <para>A second wakelock is taken as well rather than relying on
	/// Firebase's. Firebase's covers this callback; the alarm that starts
	/// underneath it needs the CPU up slightly beyond it, and a
	/// timeout-bounded partial lock costs nothing if it turns out not to
	/// be needed.</para>
	/// </remarks>
	private static void Deliver(HandAlert alert)
	{
		PowerManager.WakeLock? wakeLock = null;

		try
		{
			wakeLock = AcquireWakeLock();

			var alerts = Resolve<IAlertService>();

			// The service was started to deliver this message and the MAUI
			// container is not up yet. The alert still has to be shown:
			// there is no poll to fall back on, because the poll is a timer
			// inside the running app and the app is what is missing.
			Wait(
				alerts is not null
					? alerts.HandlePushAsync(alert)
					: PresentWithoutTheAppAsync(alert),
				alert.Id);
		}
		finally
		{
			Release(wakeLock);
		}
	}

	/// <summary>
	/// Block until the work finishes, or until the budget runs out.
	/// </summary>
	private static void Wait(Task work, long alertId)
	{
		// Firebase dispatches this callback on a background executor
		// thread, so blocking is both safe and the intent. The check is
		// defensive: blocking the main thread would deadlock against the
		// UI marshalling AlertService does through IUiDispatcher, and a
		// deadlock on the alert path is worse than a lost wakelock.
		if (MainThread.IsMainThread)
		{
			Log.Warning(
				"Alert {AlertId} was delivered on the main thread; completing without waiting",
				alertId);
			return;
		}

		try
		{
			if (!work.Wait(DeliveryBudget))
			{
				Log.Error(
					"Alert {AlertId} was not delivered within {Seconds}s",
					alertId, DeliveryBudget.TotalSeconds);
			}
		}
		catch (AggregateException ex)
		{
			// Wait wraps whatever the task threw. Unwrapped so the log says
			// what actually failed rather than "one or more errors".
			Log.Error(ex.InnerException ?? ex, "Alert {AlertId} could not be handled from push", alertId);
		}
	}

	/// <summary>
	/// Raise the notification directly, with no app behind it.
	/// </summary>
	/// <remarks>
	/// <para>The presenter needs no dependency injection — it reads the
	/// application context and posts to the alert channel — so it works
	/// from a service that started before the container did. The channel
	/// carries the alarm sound and the full-screen intent, which is what
	/// makes the handset behave like an incoming call, so this is not a
	/// degraded alert: it is the same notification the running app would
	/// have raised.</para>
	///
	/// <para>What is missing is the alarm loop and the alerts list, both
	/// of which need the app. Opening the notification starts it, and its
	/// first poll picks the alert up properly.</para>
	/// </remarks>
	private static async Task PresentWithoutTheAppAsync(HandAlert alert)
	{
		Log.Warning(
			"Alert {AlertId} arrived before the app was ready; notifying from the push service",
			alert.Id);

		// The admission rules AlertService would have applied, applied here
		// too, because this path bypasses it entirely. Shared with the
		// background poll, which reaches the same situation by a different
		// road — see HeadlessAlerts.
		await HeadlessAlerts.TryPresentAsync(alert).ConfigureAwait(false);
	}

	private static PowerManager.WakeLock? AcquireWakeLock()
	{
		try
		{
			var power = (PowerManager?)AndroidApp.Context.GetSystemService(Context.PowerService);

			// Partial: the CPU, not the screen. Waking the display is the
			// full-screen intent's job and it does it properly, over the
			// lock screen.
			var wakeLock = power?.NewWakeLock(WakeLockFlags.Partial, "hand:push-delivery");

			// Always with a timeout. An un-timed lock that leaks because
			// something threw between here and the finally would hold the
			// CPU awake for the life of the process, on a battery that has
			// to last a shift.
			wakeLock?.Acquire((long)DeliveryBudget.TotalMilliseconds * 2);

			return wakeLock;
		}
		catch (Exception ex)
		{
			// WAKE_LOCK is in the manifest, so this should not happen — and
			// if it does, delivery is still worth attempting without it.
			Log.Warning(ex, "Wakelock for push delivery could not be acquired");
			return null;
		}
	}

	private static void Release(PowerManager.WakeLock? wakeLock)
	{
		try
		{
			if (wakeLock?.IsHeld == true)
			{
				wakeLock.Release();
			}

			wakeLock?.Dispose();
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Wakelock for push delivery could not be released");
		}
	}

	/// <summary>
	/// Firebase has issued a new registration token for this install.
	/// Tokens rotate without warning, and a stale one is the usual reason
	/// a handset silently stops ringing — so this is sent on immediately.
	/// </summary>
	/// <remarks>
	/// The binding marks this obsolete because Google deprecated the
	/// overload upstream, but it is still the callback Firebase actually
	/// invokes when a token rotates and there is no replacement in this
	/// binding. Overriding it is the only way to hear about a rotation
	/// promptly; the suppression is narrow and deliberate, and
	/// <see cref="Services.DeviceAuthService.RestoreAsync"/> re-registers
	/// the token at every launch as the backstop if this ever stops
	/// firing.
	/// </remarks>
	[Obsolete("Overrides a deprecated Firebase callback; see the remarks.")]
	public override void OnNewToken(string token)
	{
#pragma warning disable CS0618 // Deprecated upstream; see the remarks above.
		base.OnNewToken(token);
#pragma warning restore CS0618

		Log.Information("Firebase issued a new registration token");

		var auth = Resolve<IDeviceAuthService>();
		if (auth is null)
		{
			// Re-registered at the next launch by DeviceAuthService.RestoreAsync,
			// which sends the current token unconditionally for this reason.
			return;
		}

		_ = Task.Run(async () =>
		{
			try
			{
				await auth.RegisterPushTokenAsync(token).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Log.Error(ex, "New push token could not be registered with Reach");
			}
		});
	}

	private static T? Resolve<T>()
		where T : class
	{
		return IPlatformApplication.Current?.Services.GetService<T>();
	}
}
