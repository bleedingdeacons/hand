using System.Text;

namespace TheBleedingDeacons.Intergroup.Hand.Support;

/// <summary>
/// Builds the descriptive <c>User-Agent</c> that every outbound request
/// introduces itself with — <c>Hand/1.19.2 (Android; rest@aa-bristol.org;
/// https://aa-bristol.org)</c>.
/// </summary>
/// <remarks>
/// <para>Set explicitly because .NET sends none of its own, which leaves
/// whatever the platform handler defaults to. On Android that is a bare
/// <c>Dalvik/2.1.0 (Linux; U; Android 16; SM-S926B Build/…)</c> — it names
/// the runtime and the handset model and says nothing about the app, so
/// every Hand handset is indistinguishable in an access log from any other
/// Android process on the network.</para>
///
/// <para>That anonymity has a cost beyond diagnostics. An unidentified
/// Dalvik client polling a REST API on a fixed interval is the shape bot
/// protection scores worst, and on 2026-09-09 SiteGround's Anti-Bot AI
/// flagged one network's address on exactly that profile: every handset
/// behind it got a JavaScript challenge page where the JSON should have
/// been, sign-in included, and a native client cannot answer one. Reach was
/// healthy throughout — the requests never reached it.</para>
///
/// <para>This is not a defence against that happening again, and must not
/// be mistaken for one: the same host fingerprints the TLS handshake (JA4),
/// which no header changes. The fix for a challenged API is the server
/// exempting it. This is here so the traffic is attributable, and so it is
/// not sitting in the anonymous bucket to start with.</para>
///
/// <para><b>The shape is Reach's own, deliberately.</b> Reach and
/// Fellowship build the same string in their <c>Core\UserAgent</c> for the
/// requests they make server-side, and it is the shape an upstream's bot
/// protection asked them for: product, version, a human to contact, and
/// which deployment the traffic belongs to. Two clients of the same API
/// introducing themselves two different ways would be two things to parse
/// in one access log for no reason. Keep this in step with
/// <c>reach/src/Core/UserAgent.php</c>.</para>
///
/// <para>The platform token is the one addition. The server has no
/// equivalent to report, and "which head is this?" is the first question
/// asked of a misbehaving handset.</para>
/// </remarks>
public static class UserAgent
{
	/// <summary>
	/// This app's product name — the first token of the header, and what a
	/// caller passing nothing falls back to.
	/// </summary>
	public const string Product = "Hand";

	/// <summary>
	/// Mailbox an upstream operator can reach a human on. Deliberately a
	/// role address, not a personal one — it outlives whoever is currently
	/// maintaining the suite. The same address Reach gives.
	/// </summary>
	public const string Contact = "rest@aa-bristol.org";

	/// <summary>
	/// What stands in for a deployment that cannot be determined — a
	/// handset that has not resolved a server address yet. Well-formed on
	/// purpose: an empty string there would leave a dangling semicolon.
	/// </summary>
	public const string UnknownEnvironment = "unknown";

	/// <summary>
	/// Build the header for one app.
	/// </summary>
	/// <param name="app">Product name, e.g. "Hand".</param>
	/// <param name="version">
	/// Product version. Left out along with its slash when empty, rather
	/// than emitting a bare "Hand/" or an invented number.
	/// </param>
	/// <param name="platform">
	/// The head this build is, e.g. "Android". Omitted when empty.
	/// </param>
	/// <param name="environment">
	/// The server this handset is pointed at, which is what distinguishes
	/// a test build's traffic from a live one's. Any trailing slash is
	/// dropped so the two spellings of a site root read as one.
	/// </param>
	public static string ForApp(string app, string version, string platform, string environment)
	{
		var name = Clean(app);
		if (name.Length == 0)
		{
			name = Product;
		}

		var release = Clean(version);
		var product = release.Length == 0 ? name : name + "/" + release;

		var head = Clean(platform);

		var where = Clean(environment).TrimEnd('/');
		if (where.Length == 0)
		{
			where = UnknownEnvironment;
		}

		var comment = head.Length == 0
			? Contact + "; " + where
			: head + "; " + Contact + "; " + where;

		return product + " (" + comment + ")";
	}

	/// <summary>
	/// Strip what would break the header: newlines and tabs, which are
	/// header injection, and the comment delimiters the format is built
	/// from. A version string carrying a stray bracket must not be able to
	/// close the comment early and leave the contact details dangling
	/// outside it.
	/// </summary>
	private static string Clean(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		var builder = new StringBuilder(value.Length);
		var collapsing = false;

		foreach (var character in value)
		{
			if (character is '\r' or '\n' or '\t')
			{
				// A run of them becomes one space, not one space each, so
				// the CRLF that made this necessary does not leave a gap
				// twice the width of a real one.
				if (!collapsing)
				{
					builder.Append(' ');
				}

				collapsing = true;
				continue;
			}

			collapsing = false;

			if (character is '(' or ')' or ';')
			{
				continue;
			}

			builder.Append(character);
		}

		return builder.ToString().Trim();
	}
}
