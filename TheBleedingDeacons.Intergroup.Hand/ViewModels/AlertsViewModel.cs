using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.ViewModels;

/// <summary>
/// The duty screen: what is outstanding, and the button that silences it.
/// </summary>
public sealed partial class AlertsViewModel : ObservableObject
{
	private readonly IAlertService _alerts;
	private readonly IDeviceAuthService _auth;
	private readonly IConfigurationService _configuration;
	private readonly IWindowVisibility _window;

	public AlertsViewModel(
		IAlertService alerts,
		IDeviceAuthService auth,
		IConfigurationService configuration,
		IWindowVisibility window)
	{
		_alerts = alerts;
		_auth = auth;
		_configuration = configuration;
		_window = window;

		Alerts.CollectionChanged += (_, _) =>
		{
			OnPropertyChanged(nameof(HasAlerts));
			OnPropertyChanged(nameof(IsClear));
			OnPropertyChanged(nameof(StatusLine));
		};

		OnDuty = _configuration.GetReachConfiguration().OnDuty;
	}

	public ObservableCollection<HandAlert> Alerts => _alerts.Active;

	public bool HasAlerts => Alerts.Count > 0;

	public bool IsClear => Alerts.Count == 0;

	public string Responder => _auth.Current?.Responder ?? string.Empty;

	/// <summary>
	/// The one line a responder reads from across a room. Deliberately
	/// states the quiet case too — "nothing outstanding" is information,
	/// and a blank screen is indistinguishable from a broken app.
	/// </summary>
	public string StatusLine => Alerts.Count switch
	{
		0 => OnDuty ? "On duty — nothing outstanding" : "Off duty",
		1 => "1 alert waiting",
		var n => $"{n} alerts waiting",
	};

	[ObservableProperty]
	public partial bool OnDuty { get; set; }

	[ObservableProperty]
	public partial bool IsRefreshing { get; set; }

	partial void OnOnDutyChanged(bool value)
	{
		OnPropertyChanged(nameof(StatusLine));

		_ = Task.Run(async () =>
		{
			try
			{
				var configuration = _configuration.GetReachConfiguration();
				configuration.OnDuty = value;
				await _configuration.SaveReachConfigurationAsync(configuration).ConfigureAwait(false);

				// Going off duty silences an alarm that is already sounding.
				// A responder switching off is asking for quiet now, not at
				// the next alert.
				if (value)
				{
					await _alerts.StartAsync().ConfigureAwait(false);
				}
				else
				{
					await _alerts.StopAsync().ConfigureAwait(false);
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Duty state could not be changed");
			}
		});
	}

	[RelayCommand]
	private async Task AcknowledgeAsync(HandAlert? alert)
	{
		if (alert is null)
		{
			return;
		}

		await _alerts.AcknowledgeAsync(alert).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task ShowContactAsync(HandAlert? alert)
	{
		if (alert is null)
		{
			return;
		}

		await _alerts.ShowContactAsync(alert).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task AcknowledgeAllAsync()
	{
		await _alerts.AcknowledgeAllAsync().ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task RefreshAsync()
	{
		IsRefreshing = true;
		try
		{
			await _alerts.RefreshAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Manual refresh failed");
		}
		finally
		{
			IsRefreshing = false;
		}
	}

	[RelayCommand]
	private static async Task OpenSettingsAsync()
	{
		await Shell.Current.GoToAsync("settings").ConfigureAwait(false);
	}

	/// <summary>
	/// Whether to offer the Hide button. False on the Apple heads, which
	/// will not let an app put itself away — see
	/// <see cref="IWindowVisibility"/>. A button that did nothing would be
	/// worse than no button.
	/// </summary>
	public bool CanHide => _window.CanHide;

	/// <summary>
	/// Put Hand out of the way without taking the handset off duty.
	///
	/// <para><b>It is not the same as leaving by the back gesture, and
	/// that is why it is here.</b> This screen is where a duty handset
	/// sits, and a responder who wants the phone to look like a phone
	/// again should be able to say so from it — without wondering whether
	/// what they just pressed also took them off the rota. Nothing about
	/// this ends the shift: the poll keeps running, the push keeps
	/// arriving, and the handset still rings.</para>
	///
	/// <para>A button of this shape used to be on the settings page,
	/// directly above Sign out, and was removed because the two looked
	/// alike and only one of them was harmless. Here there is nothing
	/// destructive within reach of it.</para>
	/// </summary>
	[RelayCommand]
	private void Hide() => _window.Hide();

	/// <summary>
	/// What arrived and what became of it.
	///
	/// <para>On the duty screen rather than behind settings, and before
	/// Settings rather than after it: the history is the more likely of
	/// the two to be wanted, and a responder reaching for it should not
	/// have to go through a page of switches to find it.</para>
	/// </summary>
	[RelayCommand]
	private static async Task OpenHistoryAsync()
	{
		await Shell.Current.GoToAsync("history").ConfigureAwait(false);
	}

	/// <summary>Re-read anything that can change while the page is away.</summary>
	public void Refresh()
	{
		OnPropertyChanged(nameof(Responder));
		OnPropertyChanged(nameof(StatusLine));
		OnPropertyChanged(nameof(HasAlerts));
		OnPropertyChanged(nameof(IsClear));
	}
}
