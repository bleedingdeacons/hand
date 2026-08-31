using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.ViewModels;

/// <summary>
/// The history page: what arrived, and what became of it.
///
/// <para>Thin on purpose. The list belongs to
/// <see cref="IAlertHistory"/> and is bound straight through, so a row
/// that changes while the page is open — an alert answered on another
/// handset a second ago — updates without the page knowing anything about
/// it.</para>
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
	private readonly IAlertHistory _history;
	private readonly IAlertService _alerts;

	public HistoryViewModel(IAlertHistory history, IAlertService alerts)
	{
		_history = history ?? throw new ArgumentNullException(nameof(history));
		_alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
	}

	public ObservableCollection<AlertHistoryEntry> Entries => _history.Entries;

	/// <summary>
	/// Whether there is anything to show. Bound by the empty state and by
	/// the Clear button, which has nothing to do on an empty list.
	/// </summary>
	public bool HasEntries => Entries.Count > 0;

	/// <summary>What the page says when nothing has arrived yet.</summary>
	public static string EmptyTitle => "Nothing yet";

	public async Task LoadAsync()
	{
		await _history.LoadAsync().ConfigureAwait(false);

		Entries.CollectionChanged -= OnEntriesChanged;
		Entries.CollectionChanged += OnEntriesChanged;

		OnPropertyChanged(nameof(HasEntries));
	}

	/// <summary>
	/// Open or close one row.
	///
	/// <para>Every row starts closed and only one thing opens it, so the
	/// list reads as a column of subjects and times — which is what makes
	/// a busy night skimmable. A row with nothing to reveal does not
	/// open at all rather than opening onto a blank space.</para>
	/// </summary>
	[RelayCommand]
	private static void Toggle(AlertHistoryEntry? entry)
	{
		if (entry is null || !entry.HasDetail)
		{
			return;
		}

		entry.IsExpanded = !entry.IsExpanded;
	}

	/// <summary>
	/// Forget everything.
	///
	/// <para>Confirmed by the page before this runs — it is not
	/// recoverable, and a mis-tap on a handset in a pocket should not cost
	/// a month of records.</para>
	/// </summary>
	[RelayCommand]
	private async Task ClearAsync()
	{
		await _history.ClearAsync().ConfigureAwait(false);

		OnPropertyChanged(nameof(HasEntries));
	}

	/// <summary>
	/// Say something about an alert after the fact.
	///
	/// <para><b>This is the whole reason reply lives on the history page
	/// as well as the duty screen.</b> When another responder answers a
	/// job first, Reach stops serving that message and Hand removes every
	/// card — so from that moment the row here is the only thing left to
	/// reply from. Reach authorises a reply on whether the alert could
	/// have been sent to this handset, never on who answered it, so it
	/// lands.</para>
	///
	/// <para>Replying changes nothing about the entry. It is not a second
	/// person taking the job on, and the row goes on saying what actually
	/// became of it.</para>
	/// </summary>
	[RelayCommand]
	private async Task ReplyAsync(AlertHistoryEntry? entry)
	{
		if (entry is null || !entry.CanReply)
		{
			return;
		}

		try
		{
			var body = await Shell.Current.DisplayPromptAsync(
				"Reply",
				"This goes to whoever sent it, and onto a lock screen. No names or numbers.",
				accept: "Send",
				cancel: "Cancel",
				maxLength: 1000);

			if (!string.IsNullOrWhiteSpace(body))
			{
				await _alerts.ReplyAsync(entry.Id, body).ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			Serilog.Log.Error(ex, "The reply could not be sent");
		}
	}

	private void OnEntriesChanged(object? sender, EventArgs e) =>
		OnPropertyChanged(nameof(HasEntries));
}
