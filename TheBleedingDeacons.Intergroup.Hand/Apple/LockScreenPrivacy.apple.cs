namespace TheBleedingDeacons.Intergroup.Hand.Services;

using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Apple half of <see cref="LockScreenPrivacy"/>, for iOS and MacCatalyst.
///
/// <para>Reports "not known", and means it rather than settling for it.</para>
///
/// <para><b>iOS has the equivalent setting and it can be read.</b>
/// Settings › Notifications › Show Previews is Always, When Unlocked, or
/// Never, and <c>UNNotificationSettings.ShowPreviewsSetting</c> surfaces
/// it: Always maps to <see cref="LockScreenPrivacyState.Shown"/>, the
/// other two to <see cref="LockScreenPrivacyState.Hidden"/>. What stops
/// this file from doing that today is the shape of the question rather
/// than the answer — reading it means
/// <c>GetNotificationSettingsAsync()</c>, and
/// <see cref="ILockScreenPrivacy.State"/> is a synchronous property
/// because the Android read is a synchronous one.</para>
///
/// <para>It is a smaller gap than it looks. These heads claim no push
/// transport at all, so no Reach push reaches an Apple lock screen; what
/// appears there comes from an alert the poll found while the app was
/// running. Worth closing when the Apple heads gain a real push
/// transport, which is the same moment several other things here become
/// worth doing.</para>
///
/// <para><b>Not reported as hidden</b>, for the reason the Windows half
/// gives: an unchecked reassurance on an intergroup's devices screen is
/// the failure this feature exists to correct.</para>
/// </summary>
public sealed partial class LockScreenPrivacy
{
	private partial string PlatformState() => LockScreenPrivacyState.Unknown;
}
