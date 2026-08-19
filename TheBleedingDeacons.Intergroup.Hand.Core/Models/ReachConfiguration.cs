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
	/// Whether this handset should keep polling and alarming. Cleared
	/// when the responder goes off duty; the app still holds its token,
	/// it just stops making noise.
	/// </summary>
	public bool OnDuty { get; set; } = true;

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
			OnDuty = OnDuty,
		};
	}
}
