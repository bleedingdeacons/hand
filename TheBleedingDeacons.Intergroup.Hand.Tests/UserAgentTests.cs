using System.Net;
using TheBleedingDeacons.Intergroup.Hand.Support;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The header every outbound request introduces itself with.
///
/// <para>The shape asserted here is the one an upstream's bot protection
/// asked Reach for — product, version, contact, deployment — with the head
/// added in front. These tests are deliberately literal about the
/// punctuation, and their counterparts in
/// <c>reach/tests/UserAgentTest.php</c> and in Link assert the same one.
/// If a change here reads as an improvement, it is a change to all
/// three.</para>
/// </summary>
public sealed class UserAgentTests
{
	private const string Server = "https://aa-bristol.org";

	private const string Expected = "Hand/1.2.3 (Android; rest@aa-bristol.org; https://aa-bristol.org)";

	[Fact]
	public void ItNamesTheApp_ItsVersion_TheHead_AContact_AndTheServer()
	{
		Assert.Equal(
			"Hand/1.2.3 (Android; rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Hand", "1.2.3", "Android", Server));
	}

	[Fact]
	public void AMissingVersionLeavesOutTheSlash()
	{
		// Better a product with no version than "Hand/" or an invented one.
		Assert.Equal(
			"Hand (Android; rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Hand", string.Empty, "Android", Server));
	}

	[Fact]
	public void AMissingPlatformLeavesOutItsToken()
	{
		// Which is the plugins' string exactly, for anything that cannot
		// tell which head it is running on.
		Assert.Equal(
			"Hand/1.2.3 (rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Hand", "1.2.3", string.Empty, Server));
	}

	[Fact]
	public void AnEmptyAppNameFallsBackToTheProduct()
	{
		Assert.StartsWith("Hand/1.0", UserAgent.ForApp(string.Empty, "1.0", "iOS", Server), StringComparison.Ordinal);
	}

	[Fact]
	public void AServerThatIsNotKnownYetReadsAsUnknown()
	{
		// A handset that has not resolved an address — a log upload on a
		// first launch, before anything has talked to Reach. Saying so
		// beats a dangling semicolon.
		Assert.Equal(
			"Hand/1.2.3 (Android; rest@aa-bristol.org; unknown)",
			UserAgent.ForApp("Hand", "1.2.3", "Android", string.Empty));
	}

	[Fact]
	public void ATrailingSlashOnTheServerIsDropped()
	{
		// The two spellings of a site root are one deployment, and should
		// not read as two in an access log.
		Assert.Equal(
			UserAgent.ForApp("Hand", "1.2.3", "Android", Server),
			UserAgent.ForApp("Hand", "1.2.3", "Android", Server + "/"));
	}

	[Fact]
	public void HeaderBreakingCharactersAreStripped()
	{
		// A newline here would be header injection; a bracket or semicolon
		// would close the comment early and leave the contact details
		// dangling outside it.
		Assert.Equal(
			"Widget/1.2.3 evil (Android; rest@aa-bristol.org; https://aa-bristol.org)",
			UserAgent.ForApp("Widget", "1.2.3\r\n(evil);", "Android", Server));
	}

	[Fact]
	public void TheContactIsTheRoleAddress()
	{
		// Not a personal one: it outlives whoever is maintaining this.
		Assert.Equal("rest@aa-bristol.org", UserAgent.Contact);
	}

	[Fact]
	public void TheHandlerRefusesANullCallback()
	{
		Assert.Throws<ArgumentNullException>(() => new UserAgentHandler(null!));
		Assert.Throws<ArgumentNullException>(() => new UserAgentHandler(null!, new RecordingHandler()));
	}

	[Fact]
	public async Task TheHandlerLabelsEveryRequest()
	{
		var (client, recorder) = Client(() => Expected);

		await client.GetAsync(new Uri("https://aa-bristol.org/one"));
		await client.GetAsync(new Uri("https://aa-bristol.org/two"));

		Assert.Equal([Expected, Expected], recorder.UserAgents);
	}

	[Fact]
	public async Task TheHandlerFollowsTheServerAddressChanging()
	{
		// The reason this is a handler at all. A responder repoints the
		// handset from the settings screen, and the next request says so
		// without the app being restarted — where a header set once at
		// startup would go on naming the old server.
		var server = "https://aa-bristol.org";
		var (client, recorder) = Client(() => UserAgent.ForApp("Hand", "1.2.3", "Android", server));

		await client.GetAsync(new Uri("https://aa-bristol.org/one"));
		server = "https://test.aa-bristol.org";
		await client.GetAsync(new Uri("https://test.aa-bristol.org/two"));

		Assert.Equal(
			[
				"Hand/1.2.3 (Android; rest@aa-bristol.org; https://aa-bristol.org)",
				"Hand/1.2.3 (Android; rest@aa-bristol.org; https://test.aa-bristol.org)",
			],
			recorder.UserAgents);
	}

	[Fact]
	public async Task TheHandlerReplacesAHeaderTheRequestAlreadyCarried()
	{
		// A retried request can arrive here having been through once
		// already, and two product tokens is not what anybody meant.
		var (client, recorder) = Client(() => Expected);

		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://aa-bristol.org/one"));
		request.Headers.UserAgent.ParseAdd("Something/0.1");

		await client.SendAsync(request);

		Assert.Equal([Expected], recorder.UserAgents);
	}

	[Fact]
	public async Task ACallbackThatThrowsDoesNotFailTheRequest()
	{
		// Whatever it reads — AppInfo, DeviceInfo, preferences — is not
		// worth losing an alert over.
		var (client, recorder) = Client(() => throw new InvalidOperationException("no MAUI here"));

		var response = await client.GetAsync(new Uri("https://aa-bristol.org/one"));

		Assert.Equal(HttpStatusCode.OK, response.StatusCode);
		Assert.Equal([string.Empty], recorder.UserAgents);
	}

	[Fact]
	public void TheSynchronousPathIsLabelledToo()
	{
		// HeadlessAlerts sends with HttpClient.Send: no app, no thread to
		// await on. That request would go out unlabelled if only SendAsync
		// were overridden.
		var (client, recorder) = Client(() => Expected);

		using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://aa-bristol.org/one"));
		using var response = client.Send(request);

		Assert.Equal([Expected], recorder.UserAgents);
	}

	private static (HttpClient Client, RecordingHandler Recorder) Client(Func<string> userAgent)
	{
		var recorder = new RecordingHandler();

		return (new HttpClient(new UserAgentHandler(userAgent, recorder)), recorder);
	}

	/// <summary>
	/// Answers everything with 200 and remembers what each request said it
	/// was. Both send paths, because the handler overrides both.
	/// </summary>
	private sealed class RecordingHandler : HttpMessageHandler
	{
		public List<string> UserAgents { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			Task.FromResult(Respond(request));

		protected override HttpResponseMessage Send(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			Respond(request);

		private HttpResponseMessage Respond(HttpRequestMessage request)
		{
			UserAgents.Add(request.Headers.UserAgent.ToString());

			return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
		}
	}
}
