using Android.App;
using Firebase.Messaging;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

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
/// <para>The one thing that defeats this is the user force-stopping the
/// app, or an OEM battery manager doing it for them. Nothing can be done
/// about the former; the latter is why the app asks to be exempted from
/// battery optimisation, and why the poll exists as well.</para>
/// </summary>
[Service(Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
public sealed class HandFirebaseMessagingService : FirebaseMessagingService
{
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

			var alert = HandAlert.FromPushData(data);
			if (alert is null)
			{
				// No usable id: the alert could never be acknowledged, so it
				// would ring forever. The poll picks it up properly instead.
				Log.Warning("Push message could not be read as an alert; leaving it to the poll");
				return;
			}

			var alerts = Resolve<IAlertService>();
			if (alerts is null)
			{
				// The service was started to deliver this message and the MAUI
				// container is not up. Nothing is lost — the alert is stored
				// server-side and the poll collects it as soon as the app runs.
				Log.Warning("Alert {AlertId} arrived before the app was ready; leaving it to the poll", alert.Id);
				return;
			}

			// Fire-and-forget: OnMessageReceived is a void platform callback
			// with a short budget, and blocking it risks the system killing
			// the service mid-alert.
			_ = Task.Run(async () =>
			{
				try
				{
					await alerts.HandlePushAsync(alert).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Log.Error(ex, "Alert {AlertId} could not be handled from push", alert.Id);
				}
			});
		}
		catch (Exception ex)
		{
			// Never throw out of a platform callback on the delivery path.
			Log.Error(ex, "Push message could not be processed");
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
