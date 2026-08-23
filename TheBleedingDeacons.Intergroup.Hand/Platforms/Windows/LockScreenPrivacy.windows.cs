namespace TheBleedingDeacons.Intergroup.Hand.Services;

using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Windows half of <see cref="LockScreenPrivacy"/>.
///
/// <para>Reports "not known", and means it. Windows has a lock screen and
/// its own notification privacy behaviour, but nothing here has looked at
/// either — and this head receives no push at all (see
/// <see cref="PushRegistrar"/>), so what reaches a Windows lock screen
/// comes from the poll rather than from Reach's push.</para>
///
/// <para><b>Not reported as hidden.</b> That would put a reassurance on an
/// intergroup's devices screen which nothing had checked, which is the
/// exact failure this whole feature exists to correct.</para>
/// </summary>
public sealed partial class LockScreenPrivacy
{
	private partial string PlatformState() => LockScreenPrivacyState.Unknown;
}
