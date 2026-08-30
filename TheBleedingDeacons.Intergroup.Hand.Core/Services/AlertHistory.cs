using System.Collections.ObjectModel;
using System.Text.Json;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Remembers what arrived and what became of it.
///
/// <para><b>Every failure here is swallowed.</b> History is a
/// convenience; alerting is the job. A handset whose storage is full, or
/// whose history file has been corrupted by a bad shutdown, must go on
/// ringing — so a read that throws yields an empty history and a write
/// that throws is logged and forgotten. Nothing in this class is allowed
/// to reach the alert loop as an exception.</para>
///
/// <para><b>Written on every change rather than on a timer or at
/// shutdown.</b> A duty handset is killed rather than closed — by the
/// user swiping it away, by an OEM battery manager, or for memory — and
/// none of those give the app a chance to flush. The file is small and
/// the writes are rare (a handful an hour on a busy night), so paying for
/// each one is cheaper than losing the night.</para>
/// </summary>
public sealed class AlertHistory : IAlertHistory
{
	/// <summary>
	/// How many alerts are kept.
	///
	/// <para>The history is cleared by hand, not by age — a responder
	/// asked "what came in over Christmas" wants it still there. The cap
	/// is a floor under that promise rather than a policy: it stops a
	/// handset nobody ever clears from growing without limit, and 500 is
	/// far more than the months of traffic a real intergroup produces.</para>
	/// </summary>
	public const int MaxEntries = 500;

	private static readonly JsonSerializerOptions Json = new()
	{
		PropertyNameCaseInsensitive = true,
	};

	private readonly IAlertHistoryStore _store;
	private readonly IUiDispatcher _dispatcher;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private bool _loaded;

	public AlertHistory(IAlertHistoryStore store, IUiDispatcher dispatcher)
	{
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
	}

	public ObservableCollection<AlertHistoryEntry> Entries { get; } = [];

	public async Task LoadAsync()
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (_loaded)
			{
				return;
			}

			_loaded = true;

			var stored = await ReadStoredAsync().ConfigureAwait(false);
			if (stored.Count == 0)
			{
				return;
			}

			await _dispatcher.InvokeAsync(() =>
			{
				foreach (var entry in stored)
				{
					Entries.Add(entry);
				}
			}).ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task RecordAsync(HandAlert alert, long receivedAt)
	{
		ArgumentNullException.ThrowIfNull(alert);

		// A notice is not a thing that happened to this handset, it is a
		// report about something that did — so it updates the row it is
		// about rather than becoming one of its own. Recording both would
		// make every answered alert two rows, one of them saying only that
		// the other was answered.
		//
		// The removal notice is an instruction to the app and never
		// reaches a responder at all.
		if (alert.IsAcknowledgementNotice || alert.IsDeviceRemoval)
		{
			return;
		}

		await MutateAsync(entries =>
		{
			if (entries.Any(e => e.Id == alert.Id))
			{
				return false;
			}

			// Newest first, which is the order the page reads in and the
			// order the cap trims from the far end of.
			entries.Insert(0, AlertHistoryEntry.From(alert, receivedAt));

			while (entries.Count > MaxEntries)
			{
				entries.RemoveAt(entries.Count - 1);
			}

			return true;
		}).ConfigureAwait(false);
	}

	public async Task SettleAsync(long alertId, string status, long settledAt, string answeredBy = "")
	{
		await MutateAsync(entries =>
		{
			var entry = entries.FirstOrDefault(e => e.Id == alertId);
			if (entry is null)
			{
				return false;
			}

			return Apply(entry, status, settledAt, answeredBy);
		}).ConfigureAwait(false);
	}

	public async Task AnsweredElsewhereAsync(string messageUuid, string answeredBy, long settledAt)
	{
		// The empty uuid is shared by everything that predates the column
		// and is not a message. Matching on it would mark the entire
		// history answered by whoever spoke first.
		if (string.IsNullOrEmpty(messageUuid))
		{
			return;
		}

		await MutateAsync(entries =>
		{
			var changed = false;

			foreach (var entry in entries.Where(e =>
				string.Equals(e.MessageUuid, messageUuid, StringComparison.Ordinal)))
			{
				changed |= Apply(entry, AlertHistoryStatus.Answered, settledAt, answeredBy);
			}

			return changed;
		}).ConfigureAwait(false);
	}

	public async Task ClearAsync()
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			await _dispatcher.InvokeAsync(Entries.Clear).ConfigureAwait(false);
			await WriteAsync().ConfigureAwait(false);
		}
		finally
		{
			_gate.Release();
		}
	}

	/// <summary>
	/// Apply an outcome to one entry, unless it already has one.
	///
	/// <para><b>The first outcome wins.</b> An alert this responder
	/// acknowledged, and which is then reported answered because the
	/// notice arrived from their own other handset, was still answered
	/// here — and a row that changed its mind about what happened would be
	/// worse than no row.</para>
	/// </summary>
	private static bool Apply(AlertHistoryEntry entry, string status, long settledAt, string answeredBy)
	{
		if (!string.Equals(entry.Status, AlertHistoryStatus.Outstanding, StringComparison.Ordinal))
		{
			return false;
		}

		entry.Status = status;
		entry.SettledAt = settledAt;
		entry.AnsweredBy = answeredBy;

		return true;
	}

	/// <summary>
	/// Change the list under the gate, on the UI thread, and store the
	/// result — but only when something actually changed.
	/// </summary>
	private async Task MutateAsync(Func<ObservableCollection<AlertHistoryEntry>, bool> change)
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			var changed = false;

			await _dispatcher.InvokeAsync(() => changed = change(Entries)).ConfigureAwait(false);

			if (changed)
			{
				await WriteAsync().ConfigureAwait(false);
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<List<AlertHistoryEntry>> ReadStoredAsync()
	{
		try
		{
			var stored = await _store.ReadAsync().ConfigureAwait(false);
			if (string.IsNullOrWhiteSpace(stored))
			{
				return [];
			}

			var entries = JsonSerializer.Deserialize<List<AlertHistoryEntry>>(stored, Json);

			return entries ?? [];
		}
		catch (Exception ex)
		{
			// A history that will not parse is a history that is gone, and
			// the app carries on without it. Logged rather than surfaced:
			// there is nothing a responder could do about it mid-shift.
			Log.Warning(ex, "Alert history could not be read; starting empty");

			return [];
		}
	}

	private async Task WriteAsync()
	{
		try
		{
			await _store.WriteAsync(JsonSerializer.Serialize(Entries, Json)).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Alert history could not be written");
		}
	}
}
