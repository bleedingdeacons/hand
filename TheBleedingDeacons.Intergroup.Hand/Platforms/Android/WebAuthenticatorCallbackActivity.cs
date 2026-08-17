using Android.App;
using Android.Content;
using Android.Content.PM;
using TheBleedingDeacons.Intergroup.Hand.Services;

namespace TheBleedingDeacons.Intergroup.Hand;

/// <summary>
/// Catches the <c>hand://auth</c> redirect at the end of an SSO sign-in
/// and hands it back to <see cref="WebAuthenticator"/>.
///
/// <para>This activity does nothing itself — the base class is the whole
/// implementation — but it has to exist, because an IntentFilter can only
/// be declared on a concrete type in this assembly. Without it Android
/// has no route from the browser tab back into the app, and
/// <c>WebAuthenticator.AuthenticateAsync</c> refuses to start at all
/// with "You must subclass the WebAuthenticatorCallbackActivity and
/// create an IntentFilter for it which matches your callbackUrl".</para>
///
/// <para><c>DataScheme</c> must match the scheme half of the callback
/// URL that <see cref="ReachClient.CallbackUri"/> sends to Reach, which
/// in turn must be on Reach's server-side allow-list
/// (<c>DeviceRedirectValidator::APP_SCHEME</c>). Those three are one
/// contract spread across two repositories; changing the scheme means
/// changing all three together.</para>
///
/// <para><c>NoHistory</c> keeps it off the back stack, so returning from
/// sign-in does not land the responder back on a blank redirect screen.
/// <c>Exported</c> is required: the browser is a different process and
/// has to be able to start it.</para>
/// </summary>
[Activity(
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop,
    Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = ReachClient.CallbackScheme)]
public class WebAuthenticatorCallbackActivity
    : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
