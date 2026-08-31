using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.ViewModels;

/// <summary>
/// Writing a message from the handset, to one member or to a committee.
///
/// <para><b>There is no "everybody" here, deliberately.</b> Reach's admin
/// screen has one and this does not: any responder may send from a
/// handset, and a broadcast is the loudest thing this system can do. The
/// server refuses a send with no recipient rather than widening it, and
/// this screen never offers the option — so nothing a tired thumb can do
/// on a phone puts the whole rota's alarms on. Putting a job *back* to
/// the rota is a different act with its own guard: see
/// <see cref="IAlertService.ResendAsync"/>.</para>
///
/// <para>The two recipient lists come from Reach and carry no addresses.
/// A member is chosen by id and resolved server-side — see
/// <see cref="HandMember"/> — which is what makes a directory on every
/// handset acceptable.</para>
/// </summary>
public sealed partial class ComposeViewModel : ObservableObject
{
	private readonly IReachClient _reach;
	private readonly IConfigurationService _configuration;

	public ComposeViewModel(IReachClient reach, IConfigurationService configuration)
	{
		_reach = reach ?? throw new ArgumentNullException(nameof(reach));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	/// <summary>Everybody the directory knows, unreachable ones included.</summary>
	public ObservableCollection<HandMember> Members { get; } = [];

	/// <summary>The committee tree, flattened, depth-first.</summary>
	public ObservableCollection<HandCommittee> Committees { get; } = [];

	[ObservableProperty]
	public partial string Subject { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Body { get; set; } = string.Empty;

	/// <summary>
	/// The search box over the member list. Re-queries the server rather
	/// than filtering what is already loaded: the directory is paged, so
	/// filtering locally would search only the first page and quietly
	/// report that somebody on the second does not exist.
	/// </summary>
	[ObservableProperty]
	public partial string Search { get; set; } = string.Empty;

	[ObservableProperty]
	public partial HandMember? SelectedMember { get; set; }

	[ObservableProperty]
	public partial HandCommittee? SelectedCommittee { get; set; }

	/// <summary>
	/// Which of the two lists is showing. Index rather than a bool because
	/// it binds a segmented control, and a third scope would otherwise
	/// mean changing the type as well as the control.
	/// </summary>
	[ObservableProperty]
	public partial int RecipientMode { get; set; }

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	[ObservableProperty]
	public partial string Status { get; set; } = string.Empty;

	/// <summary>
	/// How loud it will be.
	///
	/// <para><b>Blue by default, which is quieter than the API's own
	/// default of yellow.</b> That difference is deliberate: a plugin
	/// raises an alert because something happened, and a responder typing
	/// a message on a phone is usually passing on information. Starting
	/// at the level that wakes nobody means the loud ones are chosen on
	/// purpose rather than arrived at by leaving a control alone.</para>
	///
	/// <para>Sent explicitly on every send, so the server's default never
	/// applies here.</para>
	/// </summary>
	[ObservableProperty]
	public partial string Level { get; set; } = HandAlert.LevelBlue;

	/// <summary>
	/// Whether somebody has to take this on.
	///
	/// <para>Defaults off. The tick is the affirmative claim that this is
	/// a job rather than news, and the safe direction for a control on a
	/// phone is the one that does not silently clear a message off
	/// everybody else's screen when one person answers.</para>
	/// </summary>
	[ObservableProperty]
	public partial bool FirstToRespond { get; set; }

	public bool IsMemberMode => RecipientMode == 0;

	public bool IsCommitteeMode => RecipientMode == 1;

	/// <summary>
	/// The level radios, as three booleans.
	///
	/// <para>Plain properties rather than an equality converter over
	/// <see cref="Level"/>. A converter the toolkit does not ship compiles
	/// perfectly and then fails at runtime with "MarkupExtension not
	/// found", taking the whole page down the first time anybody opens
	/// it — which is exactly what happened. These cannot fail that
	/// way.</para>
	/// </summary>
	public bool IsLevelRed => string.Equals(Level, HandAlert.LevelRed, StringComparison.Ordinal);

	public bool IsLevelYellow => string.Equals(Level, HandAlert.LevelYellow, StringComparison.Ordinal);

	public bool IsLevelBlue => string.Equals(Level, HandAlert.LevelBlue, StringComparison.Ordinal);

	/// <summary>
	/// Whether Send should do anything: a subject, and a recipient that
	/// can actually be reached.
	///
	/// <para>An unreachable recipient is refused here as well as on the
	/// server. The server's refusal is the one that counts; this one
	/// spares a responder typing a message and pressing Send to find out.</para>
	/// </summary>
	public bool CanSend =>
		!IsBusy
		&& !string.IsNullOrWhiteSpace(Subject)
		&& (IsMemberMode
			? SelectedMember is { Reachable: true }
			: SelectedCommittee is { Reachable: true });

	public string LevelRedLabel => "Red — rings until somebody answers";

	public string LevelYellowLabel => "Yellow — makes a noise, can be missed";

	public string LevelBlueLabel => "Blue — sits in the tray, wakes nobody";

	/// <summary>
	/// Load both recipient lists. Called when the page appears.
	///
	/// <para>Failure is reported and left there rather than retried: the
	/// screen is useless without a recipient list, and a responder who can
	/// see why can go back and try again.</para>
	/// </summary>
	public async Task LoadAsync()
	{
		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			Status = "This handset is not signed in.";
			return;
		}

		await LoadMembersAsync(token).ConfigureAwait(false);

		var committees = await _reach
			.GetCommitteesAsync(token, CancellationToken.None)
			.ConfigureAwait(false);

		if (committees.Success && committees.Value is not null)
		{
			Committees.Clear();
			foreach (var committee in committees.Value)
			{
				Committees.Add(committee);
			}
		}
	}

	partial void OnRecipientModeChanged(int value)
	{
		OnPropertyChanged(nameof(IsMemberMode));
		OnPropertyChanged(nameof(IsCommitteeMode));

		// Switching scope clears the other side's choice. Leaving both set
		// would let the screen show a committee while the send addressed a
		// member — and the server refuses both at once anyway.
		if (value == 0)
		{
			SelectedCommittee = null;
		}
		else
		{
			SelectedMember = null;
		}

		OnPropertyChanged(nameof(CanSend));
	}

	partial void OnSubjectChanged(string value) => OnPropertyChanged(nameof(CanSend));

	partial void OnLevelChanged(string value)
	{
		OnPropertyChanged(nameof(IsLevelRed));
		OnPropertyChanged(nameof(IsLevelYellow));
		OnPropertyChanged(nameof(IsLevelBlue));
	}

	partial void OnSelectedMemberChanged(HandMember? value) => OnPropertyChanged(nameof(CanSend));

	partial void OnSelectedCommitteeChanged(HandCommittee? value) => OnPropertyChanged(nameof(CanSend));

	partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSend));

	partial void OnSearchChanged(string value)
	{
		_ = Task.Run(async () =>
		{
			try
			{
				var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
				if (!string.IsNullOrEmpty(token))
				{
					await LoadMembersAsync(token).ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "The member search could not be run");
			}
		});
	}

	[RelayCommand]
	private async Task SendAsync()
	{
		if (!CanSend)
		{
			return;
		}

		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			Status = "This handset is not signed in.";
			return;
		}

		IsBusy = true;
		Status = string.Empty;

		try
		{
			var result = await _reach.SendAlertAsync(
				token,
				Subject.Trim(),
				Body.Trim(),
				Level,
				FirstToRespond ? HandAlert.ResponseFirst : HandAlert.ResponseNone,
				IsMemberMode ? SelectedMember?.Id ?? 0 : 0,
				IsCommitteeMode ? SelectedCommittee?.Slug ?? string.Empty : string.Empty,
				CancellationToken.None).ConfigureAwait(false);

			if (!result.Success)
			{
				Status = result.Message.Length > 0
					? result.Message
					: "The message could not be sent.";
				return;
			}

			Log.Information("Message raised from this handset");

			await MainThread.InvokeOnMainThreadAsync(
				() => Shell.Current.GoToAsync("..")).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "The message could not be sent");
			Status = "The message could not be sent.";
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private void PickLevel(string? level)
	{
		if (!string.IsNullOrEmpty(level))
		{
			Level = level;
		}
	}

	private async Task LoadMembersAsync(string token)
	{
		var members = await _reach
			.GetMembersAsync(token, Search, page: 1, CancellationToken.None)
			.ConfigureAwait(false);

		if (!members.Success || members.Value is null)
		{
			Status = members.Message.Length > 0
				? members.Message
				: "The member list could not be loaded.";
			return;
		}

		await MainThread.InvokeOnMainThreadAsync(() =>
		{
			Members.Clear();
			foreach (var member in members.Value)
			{
				Members.Add(member);
			}
		}).ConfigureAwait(false);
	}
}
