namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Apple half of <see cref="PushRegistrar"/>.
/// </summary>
/// <remarks>
/// <para>Both halves defer to <see cref="FirebasePush"/>, which owns the
/// awkward part: iOS issues an <i>APNs device token</i>, Reach sends through
/// FCM, and <c>message.token</c> needs an <i>FCM registration token</i>.
/// Firebase exchanges one for the other.</para>
///
/// <para><b>The transport is reported from whether Firebase actually
/// started, not from whether this head was compiled with it.</b> A build
/// without <c>GoogleService-Info.plist</c>, or one whose plist names another
/// project, has no push and says so — the handset enrols poll-only.</para>
///
/// <para>That distinction is the point, and it is why this is not simply
/// <c>=> Fcm</c>. Reporting FCM when it will not work would be worse than
/// reporting nothing: the handset would enrol looking push-capable, Reach's
/// admin list would show it as "Push", the dispatcher would spend a send on
/// it for every alert, and every one of those would fail — while the
/// responder had been told their phone would ring with the app closed.
/// Poll-only is a degraded handset; a handset that lies about being
/// push-capable is a broken promise.</para>
/// </remarks>
public sealed partial class PushRegistrar
{
	private partial string PlatformProvider() => FirebasePush.Available ? Fcm : string.Empty;

	private partial Task<string> PlatformGetTokenAsync() => FirebasePush.TokenAsync();
}
