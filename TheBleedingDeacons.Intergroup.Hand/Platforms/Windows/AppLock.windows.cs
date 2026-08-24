using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Windows half of <see cref="AppLock"/>. Reports that it cannot ask,
/// which means the setting never appears on this head and the app opens
/// as it always did.
///
/// <para><b>Windows Hello is genuinely there; the prompt is what is
/// missing.</b> <c>UserConsentVerifier.CheckAvailabilityAsync</c> works
/// unchanged in a desktop app, but
/// <c>RequestVerificationAsync</c> does not: outside a UWP container
/// there is no implicit window to parent the dialog to, and it has to be
/// called through <c>IUserConsentVerifierInterop</c> with this window's
/// HWND. That is a COM interop declaration and a
/// <c>WindowNative.GetWindowHandle</c> call — perfectly doable, and
/// untested here, which is the whole reason this file says no rather
/// than half-saying yes.</para>
///
/// <para>Reporting availability and then failing to prompt would be the
/// worst of the three outcomes: a responder would turn the lock on, be
/// told it was on, and find the app opening to anyone. Saying nothing is
/// available is the honest version and costs a head where the desktop
/// sign-in has already asked who this is.</para>
/// </summary>
public sealed partial class AppLock
{
	private partial Task<bool> PlatformIsAvailableAsync() => Task.FromResult(false);

	private partial Task<AppLockResult> PlatformAuthenticateAsync(string reason) =>
		Task.FromResult(AppLockResult.Unavailable);
}
