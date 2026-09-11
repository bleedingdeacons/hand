using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;

namespace TheBleedingDeacons.Intergroup.Hand.Specs.Support;

/// <summary>
/// One handset, and everything around it, for the length of a scenario.
///
/// <para>Reqnroll gives each scenario its own instance through its
/// container, so a step class takes this in its constructor and every
/// step in the scenario sees the same handset.</para>
///
/// <para><b>The alert loop is built on first use, not in the
/// constructor.</b> A Given step can turn polling off or put the handset
/// in a meeting, and <c>AlertService</c> reads its configuration as it
/// starts — so the service has to come into being after the Givens have
/// had their say.</para>
/// </summary>
public sealed class World
{
	private readonly InlineDispatcher _dispatcher = new();

	private AlertService? _handset;
	private AlertHistory? _history;

	/// <summary>
	/// A clock a scenario can set, for the parts of the model that are
	/// asked what time it is rather than looking it up.
	///
	/// <para>The alert loop itself reads <c>DateTimeOffset.UtcNow</c>, so
	/// scenarios about a stale alert say "expired ten minutes ago" and let
	/// the real clock settle it. What this is for is
	/// <see cref="HandAlert.IsExpired"/>, which takes the instant it is
	/// judging against.</para>
	/// </summary>
	public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 11, 20, 0, 0, TimeSpan.Zero));

	public FakeConfigurationService Configuration { get; } = new();

	public FakeAlarm Alarm { get; } = new();

	public FakePresenter Presenter { get; } = new();

	public FakeReachClient Reach { get; } = new();

	public InMemoryAlertHistoryStore Store { get; } = new();

	/// <summary>Every alert this scenario has built, by its id.</summary>
	public Dictionary<long, HandAlert> Built { get; } = [];

	/// <summary>
	/// Why the handset signed itself out, or null while it is still
	/// signed in. What <c>AuthenticationLost</c> carried.
	/// </summary>
	public string? SignedOutReason { get; private set; }

	/// <summary>The last alert one of the model steps was asked about.</summary>
	public HandAlert? Subject { get; set; }

	/// <summary>The last push payload a scenario sealed or opened.</summary>
	public Dictionary<string, string>? PushData { get; set; }

	/// <summary>What <see cref="HandAlert.FromPushData"/> made of it.</summary>
	public HandAlert? Opened { get; set; }

	/// <summary>Whether the push opened at all, once it has been tried.</summary>
	public bool PushWasOpened { get; set; }

	/// <summary>The push status a scenario assembled.</summary>
	public PushStatus Status { get; set; } = PushStatus.Unknown;

	/// <summary>The configuration a scenario is examining, before it is used.</summary>
	public ReachConfiguration? Settings { get; set; }

	/// <summary>The member the directory scenarios are looking at.</summary>
	public HandMember? Member { get; set; }

	/// <summary>What the last reply or pass-back answered.</summary>
	public bool LastCallSucceeded { get; set; }

	public AlertHistory History => _history ??= new AlertHistory(Store, _dispatcher);

	public AlertService Handset
	{
		get
		{
			if (_handset is null)
			{
				_handset = new AlertService(Reach, Configuration, Alarm, Presenter, _dispatcher, History);
				_handset.AuthenticationLost += (_, e) => SignedOutReason = e.Reason;
			}

			return _handset;
		}
	}

	/// <summary>Now, as the history stamps it.</summary>
	public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	/// <summary>
	/// Kill the app and open it again, keeping only what was written down.
	///
	/// <para>A duty handset is killed rather than closed — swiped away, or
	/// taken for memory — so "does this survive a restart" is a question
	/// the history has to answer, and the only honest way to ask it is to
	/// build a second one over the same store.</para>
	/// </summary>
	public AlertHistory Restart() => _history = new AlertHistory(Store, _dispatcher);

	/// <summary>
	/// An alert of the kind a responder has to deal with.
	///
	/// <para><b>Red by default, and that is not a shortcut.</b> Most of
	/// this suite is about a handset ringing, being silenced and being
	/// answered, and only red does any of that — a fixture that defaulted
	/// to yellow would leave half these scenarios asserting against an
	/// alert that was never going to alarm.</para>
	/// </summary>
	public HandAlert Alert(
		long id,
		string level = HandAlert.LevelRed,
		string response = HandAlert.ResponseFirst,
		string kind = "shift_uncovered",
		long expiresAt = 0,
		string messageUuid = "")
	{
		var alert = new HandAlert
		{
			Id = id,
			Kind = kind,
			Source = "trusted",
			Priority = string.Equals(level, HandAlert.LevelRed, StringComparison.Ordinal) ? "urgent" : "normal",
			Level = level,
			Response = response,
			Title = "Shift uncovered",
			Body = "Nobody is on the helpline.",
			Reference = $"SHIFT-{id}",
			CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			ExpiresAt = expiresAt,
			MessageUuid = messageUuid.Length > 0 ? messageUuid : $"message-{id}",
		};

		Built[id] = alert;

		return alert;
	}

	/// <summary>
	/// The notice Reach sends everybody else when one handset
	/// acknowledges: an ordinary alert carrying the message it reports on
	/// and the name of whoever answered.
	///
	/// <para>Blue and informational because that is how Reach raises it.
	/// Nothing in Hand recognises the kind as special — it is quiet
	/// because it is blue, and it offers Close because nobody has to take
	/// it on.</para>
	/// </summary>
	public HandAlert Notice(long id, string aboutMessageUuid, string by = "Jo B", long expiresAt = 0)
	{
		var notice = Alert(
			id,
			HandAlert.LevelBlue,
			HandAlert.ResponseNone,
			HandAlert.KindMessageAcknowledged,
			expiresAt,
			$"notice-{id}");

		notice.Title = $"{by} acknowledged";
		notice.Body = "Shift uncovered";

		if (aboutMessageUuid.Length > 0)
		{
			notice.Payload[HandAlert.PayloadAckMessageUuid] = aboutMessageUuid;
		}

		if (by.Length > 0)
		{
			notice.Payload[HandAlert.PayloadAckResponder] = by;
		}

		return notice;
	}

	/// <summary>The alert on screen with this id, or null.</summary>
	public HandAlert? OnScreen(long id) => Handset.Active.FirstOrDefault(a => a.Id == id);

	/// <summary>The history row for this id, or null.</summary>
	public AlertHistoryEntry? Remembered(long id) => History.Entries.FirstOrDefault(e => e.Id == id);

	/// <summary>
	/// A feature file's "2026-09-11 20:00" as a UTC instant, so scenarios
	/// about an alert's window read as times rather than as arithmetic.
	/// </summary>
	public static DateTimeOffset Instant(string text) =>
		new(
			DateTime.ParseExact(
				text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None),
			TimeSpan.Zero);

	/// <summary>The level constant a feature file's plain word means.</summary>
	public static string Level(string word) => word.ToUpperInvariant() switch
	{
		"RED" => HandAlert.LevelRed,
		"YELLOW" => HandAlert.LevelYellow,
		"BLUE" => HandAlert.LevelBlue,
		_ => word,
	};
}
