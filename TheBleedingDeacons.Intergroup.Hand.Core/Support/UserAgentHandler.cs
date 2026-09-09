namespace TheBleedingDeacons.Intergroup.Hand.Support;

/// <summary>
/// Puts <see cref="UserAgent"/> on every request that passes through it.
/// </summary>
/// <remarks>
/// <para>A <see cref="DelegatingHandler"/> rather than
/// <c>DefaultRequestHeaders</c> on the client, because one of the four
/// things the header reports can change while the app is running: a
/// responder may repoint the handset at another server from the settings
/// screen, and <c>ConfigurationService</c> writes the new address the next
/// time anything asks for the configuration. A header baked on at startup
/// would go on naming the old server until the app was restarted, which is
/// worse than saying nothing — an access log would attribute the traffic
/// to a deployment it is not talking to.</para>
///
/// <para>The string is therefore built per request. It costs a preferences
/// read and a little string building on a path that is already making a
/// network call.</para>
///
/// <para>Nothing here throws. A request that cannot be labelled is still a
/// request worth sending, and the alternative — an exception surfacing
/// from the poll loop — is a handset that has silently stopped
/// listening.</para>
/// </remarks>
public sealed class UserAgentHandler : DelegatingHandler
{
	private readonly Func<string> _userAgent;

	/// <summary>
	/// For a client that supplies its own inner handler later, and for
	/// tests.
	/// </summary>
	public UserAgentHandler(Func<string> userAgent)
	{
		_userAgent = userAgent ?? throw new ArgumentNullException(nameof(userAgent));
	}

	/// <summary>
	/// Wraps the platform-native handler the app actually sends through.
	/// See MauiProgram for why that handler is not the managed one.
	/// </summary>
	public UserAgentHandler(Func<string> userAgent, HttpMessageHandler innerHandler)
		: base(innerHandler)
	{
		_userAgent = userAgent ?? throw new ArgumentNullException(nameof(userAgent));
	}

	protected override Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		Apply(request);

		return base.SendAsync(request, cancellationToken);
	}

	/// <summary>
	/// The synchronous path is overridden too because there is a caller on
	/// it: the headless alert reporter has no thread to await on and sends
	/// with <see cref="HttpClient.Send(HttpRequestMessage)"/>.
	/// </summary>
	protected override HttpResponseMessage Send(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		Apply(request);

		return base.Send(request, cancellationToken);
	}

	private void Apply(HttpRequestMessage request)
	{
		string value;

		try
		{
			value = _userAgent();
		}
#pragma warning disable CA1031 // Deliberately broad: see the class remarks.
		catch (Exception)
#pragma warning restore CA1031
		{
			// Whatever the callback reads — AppInfo, DeviceInfo,
			// preferences — is not worth failing a request over.
			return;
		}

		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		// Cleared first so a retried request, which reuses its message on
		// some handlers, does not end up carrying the string twice.
		request.Headers.UserAgent.Clear();

		// Discarded rather than asserted: UserAgent.ForApp strips what
		// would make this unparseable, so a false here means something
		// upstream changed — and a request with no user-agent is a better
		// answer to that than a throw.
		_ = request.Headers.UserAgent.TryParseAdd(value);
	}
}
