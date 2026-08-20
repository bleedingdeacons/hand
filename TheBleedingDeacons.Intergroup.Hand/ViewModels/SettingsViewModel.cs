using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Support;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.ViewModels;

/// <summary>
/// Where the server is, what this handset is called, and which build is
/// running.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
	private readonly IConfigurationService _configuration;
	private readonly IDeviceAuthService _auth;
	private readonly IAlertService _alerts;

	public SettingsViewModel(
		IConfigurationService configuration,
		IDeviceAuthService auth,
		IAlertService alerts)
	{
		_configuration = configuration;
		_auth = auth;
		_alerts = alerts;

		var reach = _configuration.GetReachConfiguration();
		BaseUrl = reach.BaseUrl;
		PollSeconds = reach.PollSeconds;
		Poll = reach.Poll;
		DeviceLabel = _configuration.DeviceLabel;
	}

	[ObservableProperty]
	public partial string BaseUrl { get; set; } = string.Empty;

	[ObservableProperty]
	public partial int PollSeconds { get; set; }

	/// <summary>
	/// Whether this handset asks Reach for alerts as well as listening.
	/// See <see cref="Models.ReachConfiguration.Poll"/> for what turning
	/// it off costs.
	/// </summary>
	[ObservableProperty]
	public partial bool Poll { get; set; } = true;

	[ObservableProperty]
	public partial string DeviceLabel { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = string.Empty;

	public string Responder => _auth.Current?.Responder ?? "Not signed in";

	public string DeliveryMode =>
		string.IsNullOrEmpty(_auth.Current?.PushProvider)
			? "Polling — this handset collects its own alerts"
			: "Push — alerts arrive as soon as they are raised";

	/// <summary>
	/// Version, build number, build timestamp and runtime, exactly as the
	/// startup log banner reports them, so a support conversation and the
	/// live tail agree about which build is on a handset.
	///
	/// <para><b>Instance, not static.</b> It was static, and the label on
	/// the settings screen was therefore blank: a XAML Binding resolves
	/// against the BindingContext <i>instance</i> and cannot see static
	/// members, so it bound to nothing. It failed silently in both
	/// directions — no build warning, because a binding to a missing
	/// member is only a compile error when the compiler can prove the
	/// type, and nothing on screen to notice, because an empty label
	/// looks like an empty label.</para>
	/// </summary>
	public string Build => BuildInfo.Summary;

	[RelayCommand]
	private async Task SaveAsync()
	{
		try
		{
			var configuration = _configuration.GetReachConfiguration();
			configuration.BaseUrl = BaseUrl;
			configuration.PollSeconds = PollSeconds;
			configuration.Poll = Poll;

			await _configuration.SaveReachConfigurationAsync(configuration).ConfigureAwait(false);
			_configuration.DeviceLabel = DeviceLabel;

			// Re-read so the fields show what was actually stored after
			// clamping and normalisation, rather than what was typed.
			var saved = _configuration.GetReachConfiguration();
			BaseUrl = saved.BaseUrl;
			PollSeconds = saved.PollSeconds;
			Poll = saved.Poll;

			// The poll interval and whether to poll at all are both read
			// when the loop starts, so a change to either only takes
			// effect on a restart of it. Stopping and starting is also
			// what turns the loop off: StartAsync declines to run one
			// when polling is disabled.
			await _alerts.StopAsync().ConfigureAwait(false);
			await _alerts.StartAsync().ConfigureAwait(false);

			StatusMessage = "Saved.";
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Settings could not be saved");
			StatusMessage = "Could not save those settings.";
		}
	}

	[RelayCommand]
	private async Task SignOutAsync()
	{
		try
		{
			await _alerts.StopAsync().ConfigureAwait(false);
			await _auth.SignOutAsync().ConfigureAwait(false);

			await MainThread.InvokeOnMainThreadAsync(
				() => Shell.Current.GoToAsync("//signin")).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Sign-out failed");
			StatusMessage = "Could not sign out.";
		}
	}
}
