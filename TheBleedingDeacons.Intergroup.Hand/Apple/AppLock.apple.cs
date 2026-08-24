using LocalAuthentication;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Apple half of <see cref="AppLock"/>, on LocalAuthentication.
///
/// <para>Touch ID on the handsets and Macs that have it, Face ID on the
/// ones that do not. The policy asked for is the biometric-only one:
/// <c>DeviceOwnerAuthentication</c> would fall back to the passcode and
/// make the prompt unrefusable, which the Android half declines for the
/// same reason — see its docblock.</para>
///
/// <para><b>Face ID needs a usage string.</b> An app that evaluates this
/// policy on a Face ID device without <c>NSFaceIDUsageDescription</c> in
/// its Info.plist is terminated by the system, not merely refused. Both
/// Apple heads carry the key; deleting it does not degrade this
/// gracefully.</para>
///
/// <para><b>The context is kept alive by the closure on purpose.</b>
/// <c>LAContext</c> is disposable and cancels its evaluation when it
/// goes, so a <c>using</c> here — or letting the local fall out of scope
/// before the callback runs — dismisses the prompt out from under the
/// person answering it.</para>
/// </summary>
public sealed partial class AppLock
{
	private partial Task<bool> PlatformIsAvailableAsync()
	{
		try
		{
			var context = new LAContext();
			var available = context.CanEvaluatePolicy(
				LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
				out var error);

			if (!available && error is not null)
			{
				Log.Debug("Biometrics unavailable on this device: {Reason}", error.LocalizedDescription);
			}

			return Task.FromResult(available);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Biometric availability could not be read");
			return Task.FromResult(false);
		}
	}

	private partial Task<AppLockResult> PlatformAuthenticateAsync(string reason)
	{
		var completion = new TaskCompletionSource<AppLockResult>(
			TaskCreationOptions.RunContinuationsAsynchronously);

		try
		{
			var context = new LAContext();

			if (!context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out _))
			{
				context.Dispose();
				return Task.FromResult(AppLockResult.Unavailable);
			}

			// Never empty: iOS raises an exception on a blank reason rather
			// than showing a prompt without one.
			var prompt = string.IsNullOrWhiteSpace(reason) ? "Unlock Hand" : reason;

			context.EvaluatePolicy(
				LAPolicy.DeviceOwnerAuthenticationWithBiometrics,
				prompt,
				(success, error) =>
				{
					if (!success && error is not null)
					{
						Log.Information("Fingerprint prompt refused: {Reason}", error.LocalizedDescription);
					}

					completion.TrySetResult(success ? AppLockResult.Unlocked : AppLockResult.Refused);

					// Held until here, and only here, for the reason in the
					// class docblock.
					context.Dispose();
				});
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "The fingerprint prompt could not be shown");
			return Task.FromResult(AppLockResult.Unavailable);
		}

		return completion.Task;
	}
}
