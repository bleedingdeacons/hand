using Android.Gms.Extensions;
using Firebase.Messaging;
using Serilog;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Android half of <see cref="PushRegistrar"/>: the FCM registration
/// token for this installation.
/// </summary>
public sealed partial class PushRegistrar
{
	private partial string PlatformProvider() => Fcm;

	private async partial Task<string> PlatformGetTokenAsync()
	{
		try
		{
			// GetToken is marked obsolete by the binding because Google
			// deprecated it upstream, but it remains the only way to read
			// the current registration token and the documented Android
			// guidance still uses it. Narrow suppression rather than a
			// project-wide one, so a real replacement shows up as a warning
			// here when the binding gains it.
#pragma warning disable CS0618
			var task = FirebaseMessaging.Instance.GetToken();
#pragma warning restore CS0618

			var token = await task.AsAsync<Java.Lang.String>().ConfigureAwait(false);

			return token?.ToString() ?? string.Empty;
		}
		catch (Exception ex)
		{
			// No google-services.json, Play Services missing or out of date,
			// or the device is offline on first run. All of these mean the
			// same thing to the caller: no push, so poll instead. That is a
			// degraded handset, not a broken one, so it enrols anyway.
			Log.Warning(ex, "FCM registration token could not be obtained; this handset will poll only");
			return string.Empty;
		}
	}
}
