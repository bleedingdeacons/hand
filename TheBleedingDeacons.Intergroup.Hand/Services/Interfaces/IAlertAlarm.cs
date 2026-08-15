using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// The noise and the shaking.
///
/// <para>This is the <i>in-app</i> alarm: a looping sound that keeps
/// going until a responder acknowledges it. It is what happens when Hand
/// is running.</para>
///
/// <para>When the app is closed, nothing here runs — the app is not
/// executing. The sound then comes from the operating system, played
/// from the notification channel on Android or the APNs payload on iOS,
/// and is bounded by what those allow (30 seconds on iOS, no code of
/// ours at all). <see cref="IPlatformAlertPresenter"/> is that side. The
/// two are separate because they run at genuinely different times, and
/// conflating them is how you end up with an alarm that only works when
/// somebody is already looking at the phone.</para>
/// </summary>
public interface IAlertAlarm
{
	/// <summary>Whether the alarm is currently sounding.</summary>
	bool IsSounding { get; }

	/// <summary>
	/// Start sounding for an alert. Idempotent: an alert arriving while
	/// the alarm is already going does not layer a second sound on top,
	/// because two alarms at once is just noise.
	/// </summary>
	Task StartAsync(HandAlert alert);

	/// <summary>Stop. Safe to call when nothing is sounding.</summary>
	Task StopAsync();
}
