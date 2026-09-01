using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Raises the operating system's own notification for an alert — the
/// half of alerting that survives the app not being on screen.
///
/// <para>On Android this is a full-screen-intent notification on an
/// alarm-category channel, which is what makes a handset behave like an
/// incoming call rather than a message. On Windows it is a toast with
/// looping alarm audio. On iOS the system has already displayed the APNs
/// payload by the time any of our code could run, so this only posts a
/// local notification for alerts that arrived while the app was
/// running.</para>
/// </summary>
public interface IPlatformAlertPresenter
{
	/// <summary>
	/// Ask the platform for permission to post notifications, if it needs
	/// asking. Returns whether notifications may be posted.
	///
	/// <para>Android 13+ requires POST_NOTIFICATIONS at runtime, and a
	/// responder who declines it has a handset that cannot alert them
	/// while the app is closed — so the answer matters and is surfaced,
	/// not swallowed.</para>
	/// </summary>
	Task<bool> RequestPermissionsAsync();

	/// <summary>Post the OS notification for an alert.</summary>
	/// <param name="silent">
	/// <para>Meeting mode. The notification is posted on a parallel
	/// channel with no sound; everything else about it is unchanged —
	/// same importance, same vibration, same full-screen intent on red,
	/// same Do Not Disturb bypass.</para>
	///
	/// <para><b>It has to be a second channel, not a quieter
	/// notification.</b> Android fixes a channel's sound and importance
	/// when it is created and refuses to let an app redefine them
	/// afterwards — see the EnsureChannel methods on the Android
	/// presenter, which all early-return once their channel exists. So
	/// silencing the existing one is not possible, and posting to a
	/// silent twin is the only supported way.</para>
	/// </param>
	Task PresentAsync(HandAlert alert, bool silent = false);

	/// <summary>Withdraw the notification for an acknowledged alert.</summary>
	Task DismissAsync(long alertId);
}
