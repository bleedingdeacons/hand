using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// What the handset remembers about alerts it has already dealt with.
///
/// <para>The alert loop holds only what is outstanding — see
/// <see cref="IAlertService.Active"/> — and that collection dies with the
/// process. This is the other half: a durable record of what arrived and
/// what became of it, which is what a responder needs the morning after
/// rather than during.</para>
/// </summary>
public interface IAlertHistory
{
	/// <summary>
	/// Everything remembered, newest first. Bound directly by the history
	/// page.
	/// </summary>
	ObservableCollection<AlertHistoryEntry> Entries { get; }

	/// <summary>
	/// Read the stored history into <see cref="Entries"/>. Safe to call
	/// more than once; a second call is a no-op.
	/// </summary>
	Task LoadAsync();

	/// <summary>
	/// Remember an alert that has just arrived. Ignored if this alert is
	/// already remembered, so a push and the poll that follows it do not
	/// record it twice.
	/// </summary>
	Task RecordAsync(HandAlert alert, long receivedAt);

	/// <summary>
	/// Record what became of one alert, by its id.
	///
	/// <para>Silently does nothing for an alert that was never recorded —
	/// a handset that acknowledges something from a build older than the
	/// history should not fail because of it.</para>
	/// </summary>
	Task SettleAsync(long alertId, string status, long settledAt, string answeredBy = "");

	/// <summary>
	/// Record that somebody else answered a whole message, naming them.
	/// Every remembered alert sharing the uuid is marked.
	/// </summary>
	Task AnsweredElsewhereAsync(string messageUuid, string answeredBy, long settledAt);

	/// <summary>Forget everything. What the Clear history button does.</summary>
	Task ClearAsync();
}

/// <summary>
/// Where the history is kept between runs.
///
/// <para>An interface because <c>Hand.Core</c> has no MAUI workload and
/// therefore no <c>FileSystem</c> — the same seam
/// <see cref="IConfigurationService"/> exists for. It deals in one string
/// because the shape of that string is this half's business, not the
/// platform's.</para>
/// </summary>
public interface IAlertHistoryStore
{
	/// <summary>The stored document, or empty when there is none.</summary>
	Task<string> ReadAsync();

	/// <summary>Replace the stored document.</summary>
	Task WriteAsync(string contents);
}
