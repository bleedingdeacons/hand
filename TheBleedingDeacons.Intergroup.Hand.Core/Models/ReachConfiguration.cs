namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// Where the Reach server lives, and how this handset talks to it.
///
/// <para><see cref="BaseUrl"/> is the WordPress site root — the same
/// address a responder would type into a browser. Everything else is
/// derived from it, because the endpoints are fixed by the plugin and
/// there is nothing to be gained from letting them drift apart in
/// configuration.</para>
///
/// <para><see cref="PollSeconds"/> is how often the app asks for alerts
/// while it is running. It matters most on Windows and macOS, which get
/// no push at all and for which the poll <i>is</i> the delivery
/// mechanism; on the mobile heads it is the safety net behind FCM.</para>
/// </summary>
public class ReachConfiguration
{
	/// <summary>
	/// Root URL of the WordPress site running Reach, e.g.
	/// <c>https://aa-bristol.org/</c>.
	/// </summary>
	public string BaseUrl { get; set; } = string.Empty;

	/// <summary>
	/// Seconds between polls for pending alerts.
	///
	/// <para>Twenty seconds is the default: fast enough that a desktop
	/// responder is not meaningfully behind a phone that got a push, and
	/// slow enough that a rota of handsets does not amount to a load
	/// problem on shared hosting. Clamped by <see cref="Normalised"/>
	/// rather than trusted, because a mistyped value here is a handset
	/// that either hammers the server or misses its shift.</para>
	/// </summary>
	public int PollSeconds { get; set; } = 20;

	/// <summary>
	/// Whether this handset is in a meeting: everything still happens,
	/// silently.
	///
	/// <para><b>This replaced an on/off duty switch, and the difference
	/// matters.</b> Off duty stopped the poll, so alerts did not arrive at
	/// all — a responder who forgot to come back on duty was simply
	/// missing from the rota without anybody knowing. Meeting mode changes
	/// only the volume: the poll runs, the push arrives, the card is
	/// listed, the notification is posted, the handset still vibrates and
	/// a red alert still takes the screen. What goes is the noise.</para>
	///
	/// <para><b>Off by default, which is the safe direction.</b> A handset
	/// that has never been told otherwise makes a noise, and a handset
	/// restored from a backup does too. It stays on until the responder
	/// turns it off — there is no timer — so it is a switch, not a
	/// snooze.</para>
	/// </summary>
	public bool InMeeting { get; set; }

	/// <summary>
	/// Whether this handset asks Reach for alerts, as well as listening
	/// for pushed ones.
	/// </summary>
	/// <remarks>
	/// <para><b>On by default, and turning it off is a real loss.</b> The
	/// poll is what makes the app dependable: it covers Windows and macOS
	/// entirely, it catches whatever FCM dropped while the handset was in
	/// a tunnel, and it is the only route left when a push registration
	/// token rotates silently. A handset with this off is trusting push
	/// alone, and push is the fast path, never the certain one.</para>
	///
	/// <para>It exists because "did that arrive by push or by poll?" is
	/// otherwise unanswerable from outside the app, and the two are
	/// indistinguishable to a responder watching an alert appear. With
	/// the poll off, anything that arrives came by push — which is what
	/// makes a broken push visible instead of quietly covered for.</para>
	///
	/// <para>Off does not mean silent. Pushed alerts still ring, and an
	/// explicit refresh still fetches; what stops is the automatic
	/// asking, both the in-app timer and the background one.</para>
	/// </remarks>
	public bool Poll { get; set; } = true;

	public bool IsValid()
	{
		return !string.IsNullOrWhiteSpace(BaseUrl)
			&& Uri.TryCreate(BaseUrl, UriKind.Absolute, out var parsed)
			&& (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
	}

	/// <summary>
	/// A copy with the poll interval clamped into a sane range and the
	/// base URL given a trailing slash.
	///
	/// <para>The trailing slash is not cosmetic: <c>new Uri(base,
	/// relative)</c> discards the last path segment of the base when it
	/// does not end in one, so <c>https://site/wp</c> plus
	/// <c>wp-json/…</c> silently resolves to <c>https://site/wp-json/…</c>
	/// — losing the subdirectory that a WordPress install in a folder
	/// depends on.</para>
	/// </summary>
	public ReachConfiguration Normalised()
	{
		var baseUrl = (BaseUrl ?? string.Empty).Trim();
		if (baseUrl.Length > 0 && !baseUrl.EndsWith('/'))
		{
			baseUrl += "/";
		}

		return new ReachConfiguration
		{
			BaseUrl = baseUrl,
			PollSeconds = Math.Clamp(PollSeconds, 5, 300),
			InMeeting = InMeeting,
			Poll = Poll,
		};
	}
}
