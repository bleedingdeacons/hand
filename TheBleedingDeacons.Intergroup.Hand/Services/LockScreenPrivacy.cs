using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Whether this handset's lock screen would show an alert's text.
///
/// <para>The shared half answers "not known" for the platforms where the
/// question either does not arise or cannot be asked. Android overrides
/// it under Platforms/; see <see cref="ILockScreenPrivacy"/> for why the
/// question is worth asking at all.</para>
///
/// <para><b>Not known is the honest answer for the desktop heads, not a
/// stand-in for one.</b> Windows and macOS have their own notification
/// privacy behaviour that this does not model, and reporting them as
/// "hidden" would put a reassurance on Reach's devices screen that
/// nothing had checked.</para>
/// </summary>
public sealed partial class LockScreenPrivacy : ILockScreenPrivacy
{
	public string State => PlatformState();

	private partial string PlatformState();
}
