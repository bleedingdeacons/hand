using AndroidX.Biometric;
using AndroidX.Core.Content;
using AndroidX.Fragment.App;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using AndroidApp = Android.App.Application;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Android half of <see cref="AppLock"/>, on AndroidX BiometricPrompt.
///
/// <para><b>Weak biometrics are allowed on purpose.</b> Android grades
/// its sensors, and the strong class exists so an app can tie a keystore
/// key to the result. Nothing here does: no secret is released by this
/// prompt, the device token stays exactly where it was, and the whole
/// question is whether the person holding the phone is the responder it
/// was handed to. Insisting on the strong class would only exclude
/// perfectly ordinary handsets whose face or fingerprint reader Google
/// grades as convenience, and the responder would be told to use a
/// feature their phone does not offer.</para>
///
/// <para><b>No device-credential fallback.</b> A PIN would make the
/// prompt unrefusable, which sounds like an improvement until a
/// responder with a wet thumb at 3am is typing a passcode they set two
/// years ago while the phone rings. The way out of a fingerprint that
/// will not take is the sign-out button on the lock screen, which is
/// safe precisely because signing back in needs Reach.</para>
/// </summary>
public sealed partial class AppLock
{
	private partial Task<bool> PlatformIsAvailableAsync()
	{
		try
		{
			// Synchronous on this head; the interface is asynchronous for
			// Windows' sake. See IAppLock.IsAvailableAsync.
			var manager = BiometricManager.From(AndroidApp.Context);
			var status = manager.CanAuthenticate(BiometricManager.Authenticators.BiometricWeak);

			return Task.FromResult(status == BiometricManager.BiometricSuccess);
		}
		catch (Exception ex)
		{
			// Unavailable, like every other failure here. The app opens.
			Log.Warning(ex, "Biometric availability could not be read");
			return Task.FromResult(false);
		}
	}

	private partial async Task<AppLockResult> PlatformAuthenticateAsync(string reason)
	{
		// BiometricPrompt is a fragment underneath, so it needs the activity
		// and it needs the main thread. MauiAppCompatActivity is an
		// AppCompatActivity and therefore a FragmentActivity; the cast is
		// checked rather than assumed because CurrentActivity is null for a
		// moment during a cold start, and a null there must read as "could
		// not ask", not as a crash on the launch path.
		if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not FragmentActivity activity)
		{
			Log.Warning("No activity to host the fingerprint prompt");
			return AppLockResult.Unavailable;
		}

		// The callbacks are posted to this, so it has to be the main
		// looper's. Fetched out here rather than inside the lambda because a
		// null one is an answer — "could not ask" — and not something to
		// hand to a constructor that will not take it.
		var executor = ContextCompat.GetMainExecutor(activity);

		if (executor is null)
		{
			Log.Warning("No main executor to run the fingerprint prompt on");
			return AppLockResult.Unavailable;
		}

		var completion = new TaskCompletionSource<AppLockResult>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			await MainThread.InvokeOnMainThreadAsync(() =>
			{
				var prompt = new BiometricPrompt(
					activity,
					executor,
					new PromptCallback(completion));

				var info = new BiometricPrompt.PromptInfo.Builder()
					.SetTitle("Unlock Hand")
					.SetSubtitle(reason)
					.SetNegativeButtonText("Cancel")
					// Face unlock otherwise waits for a confirm tap, which on a
					// duty handset is one more thing to do one-handed.
					.SetConfirmationRequired(false)
					.Build();

				prompt.Authenticate(info);
			}).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "The fingerprint prompt could not be shown");
			return AppLockResult.Unavailable;
		}

		return await completion.Task.ConfigureAwait(false);
	}

	/// <summary>
	/// What BiometricPrompt calls back on.
	///
	/// <para>A single result, once: the prompt raises
	/// <c>OnAuthenticationFailed</c> for every touch it does not recognise
	/// and stays on screen afterwards, so that one is deliberately not an
	/// answer. Only a success or a terminal error ends the wait, which is
	/// what <c>TrySetResult</c> is guarding.</para>
	/// </summary>
	private sealed class PromptCallback(TaskCompletionSource<AppLockResult> completion)
		: BiometricPrompt.AuthenticationCallback
	{
		public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
		{
			completion.TrySetResult(AppLockResult.Unlocked);
		}

		public override void OnAuthenticationError(int errorCode, Java.Lang.ICharSequence errString)
		{
			// Told apart because they lead opposite ways. A sensor that has
			// gone missing between the availability check and the prompt —
			// which is a real race, an enrolment can be cleared while the app
			// is starting — opens the app. Anything the holder did stays shut.
			var result = errorCode switch
			{
				BiometricPrompt.ErrorHwNotPresent
					or BiometricPrompt.ErrorHwUnavailable
					or BiometricPrompt.ErrorNoBiometrics => AppLockResult.Unavailable,
				_ => AppLockResult.Refused,
			};

			Log.Information(
				"Fingerprint prompt ended with {ErrorCode}: {Message}",
				errorCode,
				errString?.ToString() ?? string.Empty);

			completion.TrySetResult(result);
		}

		public override void OnAuthenticationFailed()
		{
			// One touch the sensor did not recognise. The prompt is still up
			// and will take another; saying anything here would close it.
		}
	}
}
