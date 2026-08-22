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
	/// The key Reach encrypts this handset's alert payloads to, or empty
	/// when it has none.
	///
	/// <para>Here for the same reason the rest of this class is: a push
	/// can arrive with no app behind it, so there is no container to ask
	/// and no <c>AlertService</c> to lean on. <c>ConfigurationService</c>
	/// reads secure storage and needs nothing injected.</para>
	///
	/// <para>Secure storage is read directly rather than through
	/// <c>ConfigurationService</c>, which takes an <c>IConfiguration</c>
	/// it has no container to supply here. The storage key is shared with
	/// that class so the two cannot drift apart.</para>
	///
	/// <para>Blocking, because <c>OnMessageReceived</c> has no async form
	/// and returning before the notification is posted is how an alert
	/// silently never arrives. Empty on any failure — an unreadable key
	/// is the same as no key, and Reach answers a handset it has no key
	/// for in plaintext, so the alert still rings.</para>
	/// </summary>
	public static string PayloadKey()
	{
		try
		{
			return SecureStorage.GetAsync(ConfigurationService.PayloadKeyKey).GetAwaiter().GetResult()
				?? string.Empty;
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Payload key could not be read while handling a push");
			return string.Empty;
		}
	}

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
