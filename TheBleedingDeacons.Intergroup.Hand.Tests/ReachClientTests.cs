using System.Net;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// The Reach REST client, driven through a stub transport.
///
/// <para>Two things are worth testing here and they pull in opposite
/// directions. One is that the client never throws: a handset on a train
/// loses signal several times a journey, and an exception escaping into
/// the poll loop is a handset that has silently stopped listening. The
/// other is that it does not flatten every refusal into "something went
/// wrong" — the difference between "try again in twenty seconds" and
/// "your certification has lapsed" is the whole reason the caller can say
/// anything useful.</para>
/// </summary>
public sealed class ReachClientTests
{
	private static (ReachClient Client, StubHttpMessageHandler Handler, FakeConfigurationService Config) Build(
		Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder,
		string baseUrl = "https://aa-bristol.org/")
	{
		var handler = new StubHttpMessageHandler(responder);
		var config = new FakeConfigurationService
		{
			Reach = new ReachConfiguration { BaseUrl = baseUrl },
		};

		return (new ReachClient(new HttpClient(handler), config), handler, config);
	}

	private static (ReachClient Client, StubHttpMessageHandler Handler) Ok(string body) =>
		Build(_ => (HttpStatusCode.OK, body)) is var (c, h, _) ? (c, h) : default;

	[Fact]
	public void Constructor_RefusesItsDependenciesBeingNull()
	{
		Assert.Throws<ArgumentNullException>(() => new ReachClient(null!, new FakeConfigurationService()));
		Assert.Throws<ArgumentNullException>(() => new ReachClient(new HttpClient(), null!));
	}

	// ── Endpoints ─────────────────────────────────────────────────────

	[Fact]
	public async Task BuildsEndpointsUnderTheConfiguredSite()
	{
		var (client, handler) = Ok("""{"alerts":[],"now":1}""");

		await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.Equal(
			"https://aa-bristol.org/wp-json/reach/v1/alerts",
			handler.Requests[0].RequestUri!.ToString());
	}

	/// <summary>
	/// A WordPress install in a subdirectory is the case that breaks if
	/// the base URL loses its trailing slash — see
	/// <see cref="ReachConfiguration.Normalised"/>.
	/// </summary>
	[Fact]
	public async Task KeepsASubdirectoryInstallsPathSegment()
	{
		var (client, handler, _) = Build(_ => (HttpStatusCode.OK, """{"alerts":[],"now":1}"""), "https://aa-bristol.org/wp/");

		await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.Equal(
			"https://aa-bristol.org/wp/wp-json/reach/v1/alerts",
			handler.Requests[0].RequestUri!.ToString());
	}

	/// <summary>
	/// An unconfigured server is not a network failure and must not be
	/// reported as one — the responder has to be sent to Settings, not
	/// told to wait for signal.
	/// </summary>
	[Fact]
	public async Task ReportsNotConfiguredRatherThanThrowingWhenThereIsNoServer()
	{
		var (client, handler, _) = Build(_ => (HttpStatusCode.OK, "{}"), baseUrl: string.Empty);

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(ReachFailure.NotConfigured, result.Failure);
		Assert.Contains("Settings", result.Message, StringComparison.Ordinal);
		Assert.Empty(handler.Requests);
	}

	[Fact]
	public void BuildSignInUrls_SendsTheRedirectForTheServerToValidate()
	{
		var (client, _, _) = Build(_ => (HttpStatusCode.OK, "{}"));

		var (start, callback) = client.BuildSignInUrls("google");

		Assert.Equal("https://aa-bristol.org/wp-json/reach/v1/auth/device/start", start.GetLeftPart(UriPartial.Path));
		Assert.Contains("provider=google", start.Query, StringComparison.Ordinal);
		Assert.Contains("redirect_uri=hand%3A%2F%2Fauth", start.Query, StringComparison.Ordinal);
		// Uri normalisation adds the empty path; what matters is that the
		// callback is the scheme the platform manifests register.
		Assert.Equal("hand", callback.Scheme);
		Assert.Equal("auth", callback.Host);
	}

	[Fact]
	public void BuildSignInUrls_EscapesTheProvider()
	{
		var (client, _, _) = Build(_ => (HttpStatusCode.OK, "{}"));

		var (start, _) = client.BuildSignInUrls("a b&c");

		Assert.Contains("provider=a%20b%26c", start.Query, StringComparison.Ordinal);
	}

	// ── Requests ──────────────────────────────────────────────────────

	/// <summary>
	/// Form encoding, not JSON: WordPress's REST layer reads form fields
	/// into request parameters natively, which is what the controllers'
	/// registered <c>args</c> validation runs against.
	/// </summary>
	[Fact]
	public async Task PostsTheEnrolmentAsFormFields()
	{
		var (client, handler) = Ok("""{"token":"abc","device_id":1}""");

		await client.SignInWithPasswordAsync(
			"dave@example.test", "hunter2", "Duty phone", "android", "fcm", "tok", CancellationToken.None);

		Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
		Assert.Equal(
			"application/x-www-form-urlencoded",
			handler.Requests[0].Content!.Headers.ContentType!.MediaType);
		Assert.Contains("email=dave%40example.test", handler.Bodies[0], StringComparison.Ordinal);
		Assert.Contains("password=hunter2", handler.Bodies[0], StringComparison.Ordinal);
		Assert.Contains("push_provider=fcm", handler.Bodies[0], StringComparison.Ordinal);
	}

	[Fact]
	public async Task SendsTheTokenAsABearerHeaderWhenThereIsOne()
	{
		var (client, handler) = Ok("""{"alerts":[],"now":1}""");

		await client.GetPendingAlertsAsync("abc123", CancellationToken.None);

		Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
		Assert.Equal("abc123", handler.Requests[0].Headers.Authorization.Parameter);
	}

	/// <summary>Enrolment happens before there is a token to send.</summary>
	[Fact]
	public async Task SendsNoAuthorisationHeaderWhenThereIsNoToken()
	{
		var (client, handler) = Ok("""{"token":"abc","device_id":1}""");

		await client.ExchangeCodeAsync("code", "label", "android", "fcm", "tok", CancellationToken.None);

		Assert.Null(handler.Requests[0].Headers.Authorization);
	}

	[Fact]
	public async Task AcknowledgementGoesToTheAlertsOwnEndpoint()
	{
		var (client, handler) = Ok("{}");

		var result = await client.AcknowledgeAsync("t", 4242, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(
			"https://aa-bristol.org/wp-json/reach/v1/alerts/4242/ack",
			handler.Requests[0].RequestUri!.ToString());
	}

	[Fact]
	public async Task SignOutAndPushUpdateCollapseToASuccessFlag()
	{
		var (client, handler) = Ok("{}");

		Assert.True((await client.SignOutAsync("t", CancellationToken.None)).Success);
		Assert.True((await client.UpdatePushTokenAsync("t", "fcm", "tok", "hidden", CancellationToken.None)).Success);
		Assert.Contains("push_provider=fcm", handler.Bodies[1], StringComparison.Ordinal);
	}

	/// <summary>
	/// The lock-screen report rides on the push registration, which Hand
	/// makes at every launch anyway.
	/// </summary>
	[Fact]
	public async Task PushUpdateCarriesTheLockScreenState()
	{
		var (client, handler) = Ok("{}");

		await client.UpdatePushTokenAsync("t", "fcm", "tok", "shown", CancellationToken.None);

		Assert.Contains("lock_screen=shown", handler.Bodies[0], StringComparison.Ordinal);
	}

    /// <summary>
    /// A handset that cannot tell sends nothing rather than sending
    /// empty. The server reads an absent field as "no news" and keeps
    /// what it already holds, so a build or a platform that cannot read
    /// the setting can never clear a warning raised by one that could.
    /// </summary>
	[Fact]
	public async Task PushUpdateOmitsAnUnknownLockScreenRatherThanSendingItEmpty()
	{
		var (client, handler) = Ok("{}");

		await client.UpdatePushTokenAsync("t", "fcm", "tok", string.Empty, CancellationToken.None);

		Assert.DoesNotContain("lock_screen", handler.Bodies[0], StringComparison.Ordinal);
	}

	// ── Responses ─────────────────────────────────────────────────────

	[Fact]
	public async Task ReadsThePendingAlertsList()
	{
		var (client, _) = Ok("""
			{"alerts":[
				{"id":1,"kind":"shift_uncovered","priority":"urgent","payload":[]},
				{"id":2,"kind":"shift_uncovered","payload":{"slot":"night"}}
			],"now":1755250000}
			""");

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(2, result.Value!.Count);
		// No level on either, so both fall back to their priority — which
		// is exactly what a Reach that predates the level sends.
		Assert.True(result.Value[0].IsUrgent);
		Assert.Equal("night", result.Value[1].Payload["slot"]);
	}

	/// <summary>
	/// The level and the response requirement come off the wire as Reach
	/// sends them. Pinned here as well as on the model because this is the
	/// route the two halves actually meet on: a rename on either side
	/// shows up as a card that is the wrong colour and a button with the
	/// wrong word on it, and nothing else says so.
	/// </summary>
	[Fact]
	public async Task ReadsTheLevelAndTheResponseRequirement()
	{
		var (client, _) = Ok("""
			{"alerts":[
				{"id":1,"kind":"call_request","level":"red","response":"first","priority":"urgent","payload":[]},
				{"id":2,"kind":"shift_reminder","level":"blue","response":"none","priority":"normal","payload":[]}
			],"now":1755250000}
			""");

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.True(result.Success);

		var job = result.Value![0];
		Assert.True(job.IsUrgent);
		Assert.False(job.IsQuiet);
		Assert.False(job.IsInformational);
		Assert.Equal("Acknowledge", job.ActionLabel);

		var reminder = result.Value[1];
		Assert.False(reminder.IsUrgent);
		Assert.True(reminder.IsQuiet);
		Assert.True(reminder.IsInformational);
		Assert.Equal("Close", reminder.ActionLabel);
	}

	/// <summary>
	/// PHP does not distinguish an integer from its decimal spelling, and
	/// WordPress hands plenty of both back from post meta. Refusing the
	/// quoted one would lose the whole response over nothing.
	/// </summary>
	[Fact]
	public async Task ReadsANumberWordPressQuoted()
	{
		var (client, _) = Ok("""{"token":"abc","device_id":"9","authorised":true}""");

		var result = await client.GetSessionAsync("t", CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal(9, result.Value!.DeviceId);
	}

	[Fact]
	public async Task ReadsTheContactDetails()
	{
		var (client, handler) = Ok("""{"alert_id":7,"contact":"07700 900000"}""");

		var result = await client.GetContactAsync("t", 7, CancellationToken.None);

		Assert.True(result.Success);
		Assert.Equal("07700 900000", result.Value);
		Assert.Equal(
			"https://aa-bristol.org/wp-json/reach/v1/alerts/7/contact",
			handler.Requests[0].RequestUri!.ToString());
	}

	[Fact]
	public async Task TreatsAnEmptyBodyOnASuccessAsAServerFault()
	{
		var (client, _) = Ok("null");

		var result = await client.GetSessionAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(ReachFailure.Server, result.Failure);
	}

	// ── Failures ──────────────────────────────────────────────────────

	/// <summary>
	/// Reach's own error code is what gets mapped, not the status —
	/// several distinct refusals share a status and the app treats them
	/// differently.
	/// </summary>
	[Theory]
	[InlineData("reach_not_eligible", 403, ReachFailure.NotEligible)]
	[InlineData("reach_invalid_credentials", 401, ReachFailure.InvalidCredentials)]
	[InlineData("reach_device_not_authenticated", 401, ReachFailure.Unauthenticated)]
	[InlineData("reach_rate_limited", 429, ReachFailure.RateLimited)]
	public async Task MapsReachsOwnErrorCode(string code, int status, ReachFailure expected)
	{
		var body = """{"code":"CODE","message":"Nope.","data":{"status":999}}"""
			.Replace("CODE", code, StringComparison.Ordinal)
			.Replace("999", status.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
		var (client, _, _) = Build(_ => ((HttpStatusCode)status, body));

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(expected, result.Failure);
		Assert.Equal("Nope.", result.Message);
	}

	/// <summary>
	/// An edge WAF or a proxy refusing on Reach's behalf sends no code at
	/// all, so the status is all there is to go on.
	/// </summary>
	[Theory]
	[InlineData(401, ReachFailure.Unauthenticated)]
	[InlineData(403, ReachFailure.NotEligible)]
	[InlineData(429, ReachFailure.RateLimited)]
	[InlineData(500, ReachFailure.Server)]
	[InlineData(404, ReachFailure.Server)]
	public async Task FallsBackToTheStatusWhenThereIsNoCode(int status, ReachFailure expected)
	{
		var (client, _, _) = Build(_ => ((HttpStatusCode)status, "<html>Forbidden</html>"));

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(expected, result.Failure);
		Assert.Contains(status.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SuppliesAMessageWhenTheServerDidNot()
	{
		var (client, _, _) = Build(_ => (HttpStatusCode.InternalServerError, """{"code":"","message":"  "}"""));

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Contains("500", result.Message, StringComparison.Ordinal);
	}

	/// <summary>
	/// Out of signal, DNS failure, timeout. The one failure worth retrying,
	/// and the one that must never escape as an exception.
	/// </summary>
	[Fact]
	public async Task ReportsATransportFailureAsNetwork()
	{
		var handler = new ThrowingHandler(new HttpRequestException("no route to host"));
		var client = new ReachClient(
			new HttpClient(handler),
			new FakeConfigurationService { Reach = new ReachConfiguration { BaseUrl = "https://aa-bristol.org/" } });

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(ReachFailure.Network, result.Failure);
	}

	/// <summary>
	/// A 200 whose body is a hosting interstitial — a WAF challenge page
	/// or a maintenance notice. It looks like success, which is exactly
	/// why it is worth distinguishing from one.
	/// </summary>
	[Fact]
	public async Task ReportsAnUnparseableSuccessBodyAsAServerFault()
	{
		var (client, _) = Ok("<html><body>Checking your browser…</body></html>");

		var result = await client.GetPendingAlertsAsync("t", CancellationToken.None);

		Assert.False(result.Success);
		Assert.Equal(ReachFailure.Server, result.Failure);
	}

	/// <summary>
	/// A deliberate cancellation — the app closing, or a superseded poll —
	/// is not a failure, and swallowing it into a "network problem" would
	/// make shutdown look like a fault.
	/// </summary>
	[Fact]
	public async Task LetsADeliberateCancellationThrough()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var handler = new ThrowingHandler(new OperationCanceledException());
		var client = new ReachClient(
			new HttpClient(handler),
			new FakeConfigurationService { Reach = new ReachConfiguration { BaseUrl = "https://aa-bristol.org/" } });

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => client.GetPendingAlertsAsync("t", cts.Token));
	}

	private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request, CancellationToken cancellationToken) =>
			throw exception;
	}
}
