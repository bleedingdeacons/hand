using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Asking this handset's owner for their fingerprint.
///
/// <para>The shared half is a pass-through and nothing else, because
/// there is no shared behaviour to speak of: every platform has its own
/// prompt, drawn and driven by the operating system, and none of them
/// hands the app anything but a yes or a no. What the yes and the no
/// mean is in <see cref="IAppLock"/>, which is the file to read
/// first.</para>
///
/// <para>Android uses AndroidX BiometricPrompt, the Apple heads use
/// LocalAuthentication, and Windows reports that it cannot ask — see
/// each half under Platforms/ and Apple/ for what that costs.</para>
/// </summary>
public sealed partial class AppLock : IAppLock
{
	public Task<bool> IsAvailableAsync() => PlatformIsAvailableAsync();

	public Task<AppLockResult> AuthenticateAsync(string reason) => PlatformAuthenticateAsync(reason);

	private partial Task<bool> PlatformIsAvailableAsync();

	private partial Task<AppLockResult> PlatformAuthenticateAsync(string reason);
}
