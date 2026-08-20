using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;

namespace TheBleedingDeacons.Intergroup.Hand.Platforms.Android;

/// <summary>
/// Raising an alert with no app behind it.
///
/// <para>Two things arrive at a handset whose process is not running: a
/// push that started the messaging service, and the background poll.
/// Neither can lean on <c>AlertService</c>, because that is part of the
/// app they are running without — so the admission rules it would have
/// applied have to be applied here instead, once, rather than copied
/// into both callers and drifting apart.</para>
///
/// <para>The presenter needs no dependency injection: it reads the
/// application context and posts to the alert channel, which carries the
/// alarm sound and the full-screen intent. So what a responder sees from
/// here is the same notification the running app would have raised, not
/// a lesser one. What is missing is the looping alarm and the alerts
/// list, both of which need the app; opening the notification starts it,
/// and its first poll takes the alert over properly.</para>
/// </summary>
internal static class HeadlessAlerts
{
	/// <summary>
	/// Show <paramref name="alert"/> if it is the kind of thing that
	/// should be shown. Returns whether it was.
	/// </summary>
	public static async Task<bool> TryPresentAsync(HandAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		// The removal notice is the one that matters here. It is an
		// instruction rather than an alert — Reach saying this handset is
		// off the rota — and it must never reach the tray or the alarm.
		// It cannot be acted on from here either, because settling it
		// means asking Reach whether it is still true, and a handset that
		// cannot ask must stay signed in. The next launch decides.
		if (alert.IsDeviceRemoval)
		{
			Log.Information("Removal notice arrived with no app running; leaving it to the next launch");
			return false;
		}

		// A push can be delivered late and a poll can surface something
		// that stopped mattering while the handset was out of signal.
		if (alert.IsExpired(DateTimeOffset.UtcNow))
		{
			Log.Debug("Alert {AlertId} arrived with no app running and had already expired", alert.Id);
			return false;
		}

		await new PlatformAlertPresenter().PresentAsync(alert).ConfigureAwait(false);
		return true;
	}
}
