using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Support;

/// <summary>
/// The handset's surroundings, stood in for.
///
/// <para>Deliberately recording doubles rather than mocks with
/// expectations: a scenario says "the handset rings" and "Reach is told",
/// and both of those read better as a question asked of a list afterwards
/// than as an expectation set up before.</para>
///
/// <para>They are a second copy of Hand.Tests' fakes, and that is
/// unavoidable rather than sloppy: those are <c>internal</c> to a test
/// project, and nothing can reference a test project. Keeping them lean
/// is the mitigation — this file covers what the feature files actually
/// ask for and nothing else.</para>
/// </summary>
public sealed class FakeConfigurationService : IConfigurationService
{
	public ReachConfiguration Reach { get; set; } =
		new() { BaseUrl = "https://example.test/", PollSeconds = 20 };

	public string DeviceToken { get; set; } = "device-token";

	public string PayloadKey { get; set; } = string.Empty;

	public int ClearCount { get; private set; }

	public BetterStackConfiguration BetterStack { get; set; } = new();

	public string DeviceLabel { get; set; } = "Duty handset";

	public bool AppLockEnabled { get; set; }

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

/// <summary>An alarm that records rather than making a noise.</summary>
public sealed class FakeAlarm : IAlertAlarm
{
	public List<HandAlert> Started { get; } = [];

	/// <summary>Whether each start was asked for silently, in order.</summary>
	public List<bool> StartedSilently { get; } = [];

	public int StopCount { get; private set; }

	public bool IsSounding { get; private set; }

	public Task StartAsync(HandAlert alert, bool silent = false)
	{
		Started.Add(alert);
		StartedSilently.Add(silent);
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
/// A presenter that records instead of raising OS notifications, and can
/// be told to throw — the alert loop is required to ring anyway.
/// </summary>
public sealed class FakePresenter : IPlatformAlertPresenter
{
	public List<HandAlert> Presented { get; } = [];

	/// <summary>Whether each notification was posted silently, in order.</summary>
	public List<bool> PresentedSilently { get; } = [];

	public List<long> Dismissed { get; } = [];

	public bool ThrowOnPresent { get; set; }

	public Task<bool> RequestPermissionsAsync() => Task.FromResult(true);

	public Task PresentAsync(HandAlert alert, bool silent = false)
	{
		PresentedSilently.Add(silent);

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
public sealed class InlineDispatcher : IUiDispatcher
{
	public Task InvokeAsync(Action action)
	{
		ArgumentNullException.ThrowIfNull(action);

		action();

		return Task.CompletedTask;
	}
}

/// <summary>
/// A scripted Reach. Each route answers whatever the scenario set on it
/// and records what it was asked, so a feature can drive the handset
/// through outcomes a real server would take a lapsed certification or a
/// tunnel to produce.
/// </summary>
public sealed class FakeReachClient : IReachClient
{
	public ReachResult<IReadOnlyList<HandAlert>> PendingAlerts { get; set; } =
		ReachResult<IReadOnlyList<HandAlert>>.Ok([]);

	public ReachResult<DeviceSession> Session { get; set; } =
		ReachResult<DeviceSession>.Ok(new DeviceSession { Authorised = true });

	public ReachResult<bool> Acknowledgement { get; set; } = ReachResult<bool>.Ok(true);

	public ReachResult<string> Contact { get; set; } = ReachResult<string>.Ok("Caller — 07700 900000");

	public ReachResult<bool> ReplyResult { get; set; } = ReachResult<bool>.Ok(true);

	public ReachResult<bool> ResendResult { get; set; } = ReachResult<bool>.Ok(true);

	public ReachResult<bool> SendResult { get; set; } = ReachResult<bool>.Ok(true);

	public ReachResult<HandMemberContact> MemberContact { get; set; } =
		ReachResult<HandMemberContact>.Ok(new HandMemberContact());

	public List<long> Acknowledged { get; } = [];

	public List<long> ContactsRequested { get; } = [];

	public List<(long AlertId, string Body)> Replies { get; } = [];

	public List<long> Resent { get; } = [];

	public List<(string Subject, string Body, string Level, string Response, long MemberId, string Committee)> Sent { get; } = [];

	public int SessionChecks { get; private set; }

	public int Polls { get; private set; }

	public int UnreadableReports { get; private set; }

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

	public Task<ReachResult<bool>> UpdatePushTokenAsync(
		string token, string pushProvider, string pushToken, string lockScreen, CancellationToken cancellationToken) =>
		Task.FromResult(ReachResult<bool>.Ok(true));

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

	public Task<ReachResult<HandMemberContact>> GetMemberContactAsync(
		string token, long memberId, CancellationToken cancellationToken) =>
		Task.FromResult(MemberContact);

	public Task<ReachResult<bool>> ReportUnreadableAsync(string token, CancellationToken cancellationToken)
	{
		UnreadableReports++;

		return Task.FromResult(ReachResult<bool>.Ok(true));
	}

	public Task<ReachResult<IReadOnlyList<HandMember>>> GetMembersAsync(
		string token, string search, int page, CancellationToken cancellationToken) =>
		Task.FromResult(ReachResult<IReadOnlyList<HandMember>>.Ok([]));

	public Task<ReachResult<IReadOnlyList<HandCommittee>>> GetCommitteesAsync(
		string token, CancellationToken cancellationToken) =>
		Task.FromResult(ReachResult<IReadOnlyList<HandCommittee>>.Ok([]));

	public Task<ReachResult<bool>> SendAlertAsync(
		string token,
		string subject,
		string body,
		string level,
		string response,
		long memberId,
		string committeeSlug,
		CancellationToken cancellationToken)
	{
		Sent.Add((subject, body, level, response, memberId, committeeSlug));

		return Task.FromResult(SendResult);
	}

	public Task<ReachResult<bool>> ReplyAsync(
		string token, long alertId, string body, CancellationToken cancellationToken)
	{
		Replies.Add((alertId, body));

		return Task.FromResult(ReplyResult);
	}

	public Task<ReachResult<bool>> ResendAsync(string token, long alertId, CancellationToken cancellationToken)
	{
		Resent.Add(alertId);

		return Task.FromResult(ResendResult);
	}
}

/// <summary>
/// Holds the history document in memory instead of on disk.
///
/// <para>A store rather than a fake <c>IAlertHistory</c> on purpose: the
/// scenarios that use it are about what gets remembered, and a stubbed
/// history would let them pass while the real one wrote nothing. The
/// actual <c>AlertHistory</c> stays in the path, JSON round trip and
/// all — which is what lets a scenario restart the app.</para>
/// </summary>
public sealed class InMemoryAlertHistoryStore : IAlertHistoryStore
{
	public string Contents { get; set; } = string.Empty;

	public int Writes { get; private set; }

	public Task<string> ReadAsync() => Task.FromResult(Contents);

	public Task WriteAsync(string contents)
	{
		Contents = contents;
		Writes++;

		return Task.CompletedTask;
	}
}
