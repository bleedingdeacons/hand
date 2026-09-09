namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// The Windows half.
///
/// <para>There is nothing to read. Windows has no per-app notification
/// permission to grant or refuse, and this head has no push transport
/// either — so the indicator settles on "not available" at the transport
/// and never reaches this. True is the honest answer to the question
/// actually being asked: nothing here is withholding permission.</para>
/// </summary>
public sealed partial class NotificationPermission
{
	private partial Task<bool> PlatformIsGrantedAsync() => Task.FromResult(true);
}
