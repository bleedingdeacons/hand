using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services;
using Xunit;

namespace TheBleedingDeacons.Intergroup.Hand.Tests;

/// <summary>
/// What the handset remembers after the fact.
///
/// <para>Two rules run through all of it. A record of what happened is
/// only worth having if it survives the process — a duty handset is
/// killed rather than closed — and it must never be able to stop the app
/// alerting, so every storage failure is swallowed.</para>
/// </summary>
public class AlertHistoryTests
{
	private readonly InlineDispatcher _dispatcher = new();
	private readonly InMemoryAlertHistoryStore _store = new();

	private AlertHistory Build() => new(_store, _dispatcher);

	// ── Recording ─────────────────────────────────────────────────────

	[Fact]
	public async Task AnArrivingAlertIsRemembered()
	{
		var history = Build();
		await history.LoadAsync();

		await history.RecordAsync(Alerts.New(7), 1_700_000_000);

		var entry = Assert.Single(history.Entries);
		Assert.Equal(7, entry.Id);
		Assert.Equal("Shift uncovered", entry.Subject);
		Assert.Equal(HandAlert.LevelRed, entry.Level);
		Assert.Equal(AlertHistoryStatus.Outstanding, entry.Status);
	}

	[Fact]
	public async Task TheSameAlertIsNotRememberedTwice()
	{
		// A push and the poll that follows it are one alert. The alert
		// loop already resolves that, but a second route into the history
		// would undo it.
		var history = Build();
		await history.LoadAsync();

		await history.RecordAsync(Alerts.New(7), 1);
		await history.RecordAsync(Alerts.New(7), 2);

		Assert.Single(history.Entries);
	}

	[Fact]
	public async Task NewestIsFirst()
	{
		var history = Build();
		await history.LoadAsync();

		await history.RecordAsync(Alerts.New(1), 1);
		await history.RecordAsync(Alerts.New(2), 2);

		Assert.Equal([2L, 1L], history.Entries.Select(e => e.Id));
	}

	[Fact]
	public async Task ANoticeIsNotARowOfItsOwn()
	{
		// It reports on something that happened rather than being one, so
		// it updates the row it is about. Recorded as well, every answered
		// alert would be two rows, one of them saying only that the other
		// was answered.
		var history = Build();
		await history.LoadAsync();

		await history.RecordAsync(Alerts.Notice(9, "m1"), 1);

		Assert.Empty(history.Entries);
	}

	[Fact]
	public async Task AnInformationalAlertIsRememberedAsClosed()
	{
		// Nobody had to take it on, so there was never anything
		// outstanding about it to resolve.
		var history = Build();
		await history.LoadAsync();

		await history.RecordAsync(
			Alerts.New(7, response: HandAlert.ResponseNone),
			1_700_000_000);

		Assert.Equal(AlertHistoryStatus.Closed, history.Entries[0].Status);
	}

	// ── Outcomes ──────────────────────────────────────────────────────

	[Fact]
	public async Task AnAcknowledgedAlertSaysSo()
	{
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7), 1);

		await history.SettleAsync(7, AlertHistoryStatus.Acknowledged, 50);

		Assert.Equal(AlertHistoryStatus.Acknowledged, history.Entries[0].Status);
		Assert.Equal(50, history.Entries[0].SettledAt);
		Assert.Equal("Acknowledged by you", history.Entries[0].StatusLine);
	}

	[Fact]
	public async Task AnAlertAnsweredElsewhereNamesWhoAnsweredIt()
	{
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7, messageUuid: "m1"), 1);

		await history.AnsweredElsewhereAsync("m1", "Jo B", 50);

		Assert.Equal(AlertHistoryStatus.Answered, history.Entries[0].Status);
		Assert.Equal("Answered by Jo B", history.Entries[0].StatusLine);
	}

	[Fact]
	public async Task EveryCopyOfTheMessageIsMarked()
	{
		// One message to a responder holding two handsets is two alerts.
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7, messageUuid: "m1"), 1);
		await history.RecordAsync(Alerts.New(8, messageUuid: "m1"), 2);

		await history.AnsweredElsewhereAsync("m1", "Jo B", 50);

		Assert.All(history.Entries, e => Assert.Equal(AlertHistoryStatus.Answered, e.Status));
	}

	[Fact]
	public async Task TheEmptyUuidMarksNothing()
	{
		// Everything written before that column existed shares it and is
		// not one message. Matching on it would mark the whole history
		// answered by whoever spoke first.
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7), 1);

		await history.AnsweredElsewhereAsync(string.Empty, "Jo B", 50);

		Assert.Equal(AlertHistoryStatus.Outstanding, history.Entries[0].Status);
	}

	[Fact]
	public async Task TheFirstOutcomeWins()
	{
		// An alert this responder acknowledged, then reported answered
		// because the notice came from their own other handset, was still
		// answered here — and a row that changed its mind about what
		// happened is worse than no row.
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7, messageUuid: "m1"), 1);

		await history.SettleAsync(7, AlertHistoryStatus.Acknowledged, 50);
		await history.AnsweredElsewhereAsync("m1", "Jo B", 60);

		Assert.Equal(AlertHistoryStatus.Acknowledged, history.Entries[0].Status);
		Assert.Equal(50, history.Entries[0].SettledAt);
	}

	[Fact]
	public async Task SettlingAnAlertNobodyRememberedIsNotAnError()
	{
		// A handset acknowledging something recorded by a build older than
		// the history should not fail because of it.
		var history = Build();
		await history.LoadAsync();

		await history.SettleAsync(99, AlertHistoryStatus.Acknowledged, 50);

		Assert.Empty(history.Entries);
	}

	// ── Persistence ───────────────────────────────────────────────────

	[Fact]
	public async Task HistorySurvivesARestart()
	{
		// The whole point: a duty handset is killed rather than closed, so
		// anything held only in memory is gone by morning.
		var first = Build();
		await first.LoadAsync();
		await first.RecordAsync(Alerts.New(7), 1_700_000_000);
		await first.SettleAsync(7, AlertHistoryStatus.Acknowledged, 1_700_000_060);

		var second = Build();
		await second.LoadAsync();

		var entry = Assert.Single(second.Entries);
		Assert.Equal(7, entry.Id);
		Assert.Equal("Shift uncovered", entry.Subject);
		Assert.Equal(AlertHistoryStatus.Acknowledged, entry.Status);
	}

	[Fact]
	public async Task RowsComeBackClosed()
	{
		// Open/closed is view state. A history that reopened yesterday's
		// rows on every launch would not be skimmable.
		var first = Build();
		await first.LoadAsync();
		await first.RecordAsync(Alerts.New(7), 1);
		first.Entries[0].IsExpanded = true;

		var second = Build();
		await second.LoadAsync();

		Assert.False(second.Entries[0].IsExpanded);
	}

	[Fact]
	public async Task LoadingTwiceDoesNotDuplicate()
	{
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7), 1);

		await history.LoadAsync();

		Assert.Single(history.Entries);
	}

	[Fact]
	public async Task NothingIsWrittenWhenNothingChanged()
	{
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7), 1);

		var writes = _store.Writes;
		await history.RecordAsync(Alerts.New(7), 2);
		await history.SettleAsync(99, AlertHistoryStatus.Acknowledged, 3);

		Assert.Equal(writes, _store.Writes);
	}

	// ── Clearing ──────────────────────────────────────────────────────

	[Fact]
	public async Task ClearingForgetsEverything()
	{
		var history = Build();
		await history.LoadAsync();
		await history.RecordAsync(Alerts.New(7), 1);

		await history.ClearAsync();

		Assert.Empty(history.Entries);

		var reloaded = Build();
		await reloaded.LoadAsync();
		Assert.Empty(reloaded.Entries);
	}

	// ── Limits and failure ────────────────────────────────────────────

	[Fact]
	public async Task TheOldestAreDroppedPastTheCap()
	{
		// Cleared by hand rather than by age, so the cap is a floor under
		// that promise: it stops a handset nobody ever clears growing
		// without limit.
		var history = Build();
		await history.LoadAsync();

		for (var id = 1; id <= AlertHistory.MaxEntries + 5; id++)
		{
			await history.RecordAsync(Alerts.New(id), id);
		}

		Assert.Equal(AlertHistory.MaxEntries, history.Entries.Count);

		// Newest kept, oldest gone.
		Assert.Equal(AlertHistory.MaxEntries + 5, history.Entries[0].Id);
		Assert.DoesNotContain(history.Entries, e => e.Id == 1);
	}

	[Fact]
	public async Task AStoreThatCannotBeReadStartsEmptyRatherThanThrowing()
	{
		// History is a convenience; alerting is the job.
		var history = new AlertHistory(new BrokenAlertHistoryStore(), _dispatcher);

		await history.LoadAsync();

		Assert.Empty(history.Entries);
	}

	[Fact]
	public async Task AStoreThatCannotBeWrittenDoesNotStopTheAlert()
	{
		var history = new AlertHistory(new BrokenAlertHistoryStore(), _dispatcher);
		await history.LoadAsync();

		await history.RecordAsync(Alerts.New(7), 1);

		// Remembered for this run even though it could not be stored.
		Assert.Single(history.Entries);
	}

	[Fact]
	public async Task AStoredDocumentThatWillNotParseStartsEmpty()
	{
		_store.Contents = "{ not json";

		var history = Build();
		await history.LoadAsync();

		Assert.Empty(history.Entries);
	}

	[Fact]
	public void Constructor_RefusesItsDependenciesBeingNull()
	{
		Assert.Throws<ArgumentNullException>(() => new AlertHistory(null!, _dispatcher));
		Assert.Throws<ArgumentNullException>(() => new AlertHistory(_store, null!));
	}
}
