using System.Net;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// A stub <see cref="HttpMessageHandler"/> that answers from a
/// caller-supplied responder, so a real <c>ReachClient</c> can be driven
/// without a live server. Records what it was asked for, because half of
/// what these tests check is the request rather than the response.
/// </summary>
internal sealed class StubHttpMessageHandler(
	Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder)
	: HttpMessageHandler
{
	/// <summary>Every request that reached the handler, in order.</summary>
	public List<HttpRequestMessage> Requests { get; } = [];

	/// <summary>The form bodies of those requests, in the same order.</summary>
	public List<string> Bodies { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		Bodies.Add(request.Content is null
			? string.Empty
			: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

		var (status, body) = responder(request);

		return new HttpResponseMessage(status)
		{
			Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
			RequestMessage = request,
		};
	}

	/// <summary>A handler that gives the same answer to everything.</summary>
	public static StubHttpMessageHandler Always(HttpStatusCode status, string body) =>
		new(_ => (status, body));
}

/// <summary>
/// A configuration service backed by fields rather than Preferences and
/// SecureStorage, which are MAUI and therefore out of reach here.
/// </summary>
internal sealed class FakeConfigurationService : IConfigurationService
{
	public ReachConfiguration Reach { get; set; } = new() { BaseUrl = "https://example.test/", PollSeconds = 20, OnDuty = true };

	public string DeviceToken { get; set; } = string.Empty;

	public string PayloadKey { get; set; } = string.Empty;

	public int ClearCount { get; private set; }

	public BetterStackConfiguration BetterStack { get; set; } = new();

	public string DeviceLabel { get; set; } = "Test handset";

	public ReachConfiguration GetReachConfiguration() => Reach;

	public Task SaveReachConfigurationAsync(ReachConfiguration configuration)
	{
		Reach = configuration.Normalised();
		return Task.CompletedTask;
	}

	public Task<string> GetDeviceTokenAsync() => Task.FromResult(DeviceToken);

	public Task SaveDeviceTokenAsync(string token)
	{
		DeviceToken = token;
		return Task.CompletedTask;
	}

	public Task ClearDeviceTokenAsync()
	{
		ClearCount++;
		DeviceToken = string.Empty;
		return Task.CompletedTask;
	}

	public Task<string> GetPayloadKeyAsync() => Task.FromResult(PayloadKey);

	public Task SavePayloadKeyAsync(string key)
	{
		PayloadKey = key;
		return Task.CompletedTask;
	}

	public Task ClearPayloadKeyAsync()
	{
		PayloadKey = string.Empty;
		return Task.CompletedTask;
	}

	public BetterStackConfiguration GetBetterStackConfiguration() => BetterStack;
}

/// <summary>
/// An alarm that records what it was told to do instead of making a noise.
/// </summary>
internal sealed class FakeAlarm : IAlertAlarm
{
	public List<HandAlert> Started { get; } = [];

	public int StopCount { get; private set; }

	public bool IsSounding { get; private set; }

	public Task StartAsync(HandAlert alert)
	{
		Started.Add(alert);
		IsSounding = true;
		return Task.CompletedTask;
	}

	public Task StopAsync()
	{
		StopCount++;
		IsSounding = false;
		return Task.CompletedTask;
	}
}

/// <summary>
/// A presenter that records rather than raising OS notifications, and can
/// be told to throw — the alert loop is required to survive that.
/// </summary>
internal sealed class FakePresenter : IPlatformAlertPresenter
{
	public List<HandAlert> Presented { get; } = [];

	public List<long> Dismissed { get; } = [];

	public bool ThrowOnPresent { get; set; }

	public Task<bool> RequestPermissionsAsync() => Task.FromResult(true);

	public Task PresentAsync(HandAlert alert)
	{
		if (ThrowOnPresent)
		{
			throw new InvalidOperationException("no notification permission");
		}

		Presented.Add(alert);
		return Task.CompletedTask;
	}

	public Task DismissAsync(long alertId)
	{
		Dismissed.Add(alertId);
		return Task.CompletedTask;
	}
}

/// <summary>
/// Runs the work inline. There is no UI thread in a test host, and the
/// contract only requires the action to have completed by the time the
/// task does.
/// </summary>
internal sealed class InlineDispatcher : IUiDispatcher
{
	public int Invocations { get; private set; }

	public Task InvokeAsync(Action action)
	{
		Invocations++;
		action();
		return Task.CompletedTask;
	}
}

/// <summary>
/// A scripted <see cref="IReachClient"/>. Each endpoint hands back
/// whatever the test set on it and records the calls, so the alert loop
/// can be driven through outcomes — a lapsed certification, a 401, a
/// network drop — that a stub HTTP handler would only reach indirectly.
/// </summary>
internal sealed class FakeReachClient : IReachClient
{
	public ReachResult<IReadOnlyList<HandAlert>> PendingAlerts { get; set; } =
		ReachResult<IReadOnlyList<HandAlert>>.Ok([]);

	public ReachResult<DeviceSession> Session { get; set; } =
		ReachResult<DeviceSession>.Ok(new DeviceSession());

	public ReachResult<bool> Acknowledgement { get; set; } = ReachResult<bool>.Ok(true);

	public ReachResult<string> Contact { get; set; } = ReachResult<string>.Ok("07700 900000");

	public List<long> Acknowledged { get; } = [];

	public List<long> ContactsRequested { get; } = [];

	public int SessionChecks { get; private set; }

	public int Polls { get; private set; }

	public (Uri Start, Uri Callback) BuildSignInUrls(string provider) =>
		(new Uri("https://example.test/start"), new Uri("hand://auth"));

	public Task<ReachResult<DeviceSession>> ExchangeCodeAsync(
		string code, string label, string platform, string pushProvider, string pushToken, CancellationToken cancellationToken) =>
		Task.FromResult(Session);

	public Task<ReachResult<DeviceSession>> SignInWithPasswordAsync(
		string email, string password, string label, string platform, string pushProvider, string pushToken, CancellationToken cancellationToken) =>
		Task.FromResult(Session);

	public Task<ReachResult<DeviceSession>> GetSessionAsync(string token, CancellationToken cancellationToken)
	{
		SessionChecks++;
		return Task.FromResult(Session);
	}

	/// <summary>The lock-screen state of the last push registration.</summary>
	public string LastLockScreen { get; private set; } = "not called";

	public Task<ReachResult<bool>> UpdatePushTokenAsync(
		string token,
		string pushProvider,
		string pushToken,
		string lockScreen,
		CancellationToken cancellationToken)
	{
		LastLockScreen = lockScreen;

		return Task.FromResult(ReachResult<bool>.Ok(true));
	}

	public Task<ReachResult<bool>> SignOutAsync(string token, CancellationToken cancellationToken) =>
		Task.FromResult(ReachResult<bool>.Ok(true));

	public Task<ReachResult<IReadOnlyList<HandAlert>>> GetPendingAlertsAsync(
		string token, CancellationToken cancellationToken)
	{
		Polls++;
		return Task.FromResult(PendingAlerts);
	}

	public Task<ReachResult<bool>> AcknowledgeAsync(string token, long alertId, CancellationToken cancellationToken)
	{
		Acknowledged.Add(alertId);
		return Task.FromResult(Acknowledgement);
	}

	public Task<ReachResult<string>> GetContactAsync(string token, long alertId, CancellationToken cancellationToken)
	{
		ContactsRequested.Add(alertId);
		return Task.FromResult(Contact);
	}

	/// <summary>How many times the handset said it cannot read its alerts.</summary>
	public int UnreadableReports { get; private set; }

	public Task<ReachResult<bool>> ReportUnreadableAsync(string token, CancellationToken cancellationToken)
	{
		UnreadableReports++;
		return Task.FromResult(ReachResult<bool>.Ok(true));
	}
}

/// <summary>Shorthand for building alerts in tests.</summary>
internal static class Alerts
{
	public static HandAlert New(long id = 1, string kind = "shift_uncovered", long expiresAt = 0) =>
		new()
		{
			Id = id,
			Kind = kind,
			Source = "trusted",
			Priority = "normal",
			Title = "Shift uncovered",
			Body = "Nobody is on the helpline.",
			Reference = $"SHIFT-{id}",
			CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			ExpiresAt = expiresAt,
		};
}
