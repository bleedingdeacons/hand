using System.Net.Http.Headers;
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
	/// is the same as no key, and either way the push cannot be opened
	/// and is reported rather than shown; see
	/// <see cref="ReportUnreadable"/>.</para>
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
	/// Tell Reach this handset cannot open what it is sent, with no app
	/// behind the report.
	/// </summary>
	/// <remarks>
	/// <para><b>Why "ignored" does not mean "silent".</b> A push that will
	/// not open is not shown to the responder — there is nothing to show,
	/// and inventing a fault notice to wake someone with was the thing
	/// this replaced. But a handset that quietly stops ringing is exactly
	/// the failure that has to be visible, and this report is what puts it
	/// on Reach's devices screen. The alert itself is not lost: the poll
	/// is unencrypted HTTPS to our own server and still delivers it.</para>
	///
	/// <para>Everything is read straight out of secure storage and
	/// preferences, on the precedent <see cref="PayloadKey"/> already
	/// sets: this runs on a push that started the messaging service, so
	/// there may be no container to resolve <c>IReachClient</c> from and
	/// no <c>ConfigurationService</c> to ask. The keys are shared with
	/// that class so the two readers cannot drift on to different
	/// entries.</para>
	///
	/// <para>Blocking and bounded, for the same reason as everything else
	/// on this path, and silent on every failure. It is a diagnostic; a
	/// handset that cannot reach the server has a larger problem, which
	/// its own logging already covers.</para>
	/// </remarks>
	public static void ReportUnreadable()
	{
		try
		{
			var token = SecureStorage.GetAsync(ConfigurationService.DeviceTokenKey)
				.GetAwaiter().GetResult() ?? string.Empty;

			// Signed out. There is nothing to authenticate the report with,
			// and an unauthenticated one would be refused anyway.
			if (token.Length == 0)
			{
				return;
			}

			var baseUrl = Preferences.Get(ConfigurationService.ReachResolvedBaseUrlKey, string.Empty);
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root))
			{
				Log.Warning("This handset cannot read its alerts, and has no server address to say so to");
				return;
			}

			using var http = new HttpClient { Timeout = ReportBudget };
			using var request = new HttpRequestMessage(
				HttpMethod.Post,
				new Uri(root, "wp-json/reach/v1/alerts/unreadable"))
			{
				// Form-encoded and empty, matching ReachClient: WordPress's
				// REST layer reads form fields into request parameters, which
				// is what the controllers' registered validation runs against.
				Content = new FormUrlEncodedContent([]),
			};

			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

			using var response = http.Send(request);

			Log.Warning(
				"Told Reach this handset cannot read its alerts ({Status})",
				(int)response.StatusCode);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Could not tell Reach this handset cannot read its alerts");
		}
	}

	/// <summary>
	/// How long the fault report may take before it is abandoned.
	///
	/// <para>Well inside the messaging service's own delivery budget,
	/// because this runs on the same callback and the wakelock behind it
	/// is not ours to overrun. Nothing is lost by giving up: the next
	/// push reports again, the flag that suppresses repeats living in the
	/// running app rather than here.</para>
	/// </summary>
	private static readonly TimeSpan ReportBudget = TimeSpan.FromSeconds(5);

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
