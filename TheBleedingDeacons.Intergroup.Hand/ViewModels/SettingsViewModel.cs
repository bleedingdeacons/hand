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
	private readonly IAppLock _lock;
	private readonly IWindowVisibility _window;

	public SettingsViewModel(
		IConfigurationService configuration,
		IDeviceAuthService auth,
		IAlertService alerts,
		IAppLock appLock,
		IWindowVisibility window)
	{
		_configuration = configuration;
		_auth = auth;
		_alerts = alerts;
		_lock = appLock;
		_window = window;

		var reach = _configuration.GetReachConfiguration();
		BaseUrl = reach.BaseUrl;
		PollSeconds = reach.PollSeconds;
		Poll = reach.Poll;
		DeviceLabel = _configuration.DeviceLabel;
		RequireFingerprint = _configuration.AppLockEnabled;
	}

	/// <summary>
	/// Find out whether this handset can ask for a fingerprint at all.
	///
	/// <para>Not in the constructor because the answer is asynchronous on
	/// at least one head, and not cached across visits because a responder
	/// who has just been to the phone's own settings to enrol a finger
	/// should find the checkbox live when they come back.</para>
	/// </summary>
	public async Task LoadAsync()
	{
		try
		{
			// ConfigureAwait(true): called from OnAppearing, and the property
			// set below drives a checkbox on screen.
			FingerprintAvailable = await _lock.IsAvailableAsync().ConfigureAwait(true);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Fingerprint availability could not be read");
			FingerprintAvailable = false;
		}
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

	/// <summary>
	/// Whether opening Hand should ask for a fingerprint first.
	///
	/// <para>Saved with everything else on this page rather than the
	/// moment it is ticked, and proved before it is saved — see
	/// <see cref="SaveAsync"/> for why turning it on asks for the
	/// fingerprint there and then.</para>
	/// </summary>
	[ObservableProperty]
	public partial bool RequireFingerprint { get; set; }

	/// <summary>
	/// Whether there is a fingerprint on this handset to ask for. False
	/// leaves the checkbox on screen but disabled: a setting that
	/// disappears is a setting a responder thinks they imagined.
	/// </summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(FingerprintUnavailable))]
	public partial bool FingerprintAvailable { get; set; }

	public bool FingerprintUnavailable => !FingerprintAvailable;

	/// <summary>
	/// Whether to offer the Close button. False on the Apple heads, which
	/// will not let an app put itself away — see
	/// <see cref="IWindowVisibility"/>.
	/// </summary>
	public bool CanHide => _window.CanHide;

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

			// Turning the lock on is proved before it is stored. A responder
			// who ticks this box, presses Save, and only discovers at the next
			// launch that the sensor will not take their finger has been let
			// down by this screen rather than by the sensor — so the ask
			// happens here, while there is somebody looking at it who can be
			// told. Turning it off is not proved: nobody has to prove they are
			// allowed to make a handset easier to open, and requiring it would
			// trap a responder whose finger has stopped working.
			string? lockRefusal = null;

			if (RequireFingerprint && !_configuration.AppLockEnabled)
			{
				var result = await _lock
					.AuthenticateAsync("Confirm the fingerprint that will open Hand")
					.ConfigureAwait(false);

				lockRefusal = result switch
				{
					AppLockResult.Unlocked => null,
					AppLockResult.Unavailable =>
						"Saved, but the fingerprint lock is off: this handset cannot ask for one.",
					_ =>
						"Saved, but the fingerprint lock is off: the fingerprint was not confirmed.",
				};
			}

			_configuration.AppLockEnabled = RequireFingerprint && lockRefusal is null;

			// Re-read so the fields show what was actually stored after
			// clamping and normalisation, rather than what was typed.
			var saved = _configuration.GetReachConfiguration();
			BaseUrl = saved.BaseUrl;
			PollSeconds = saved.PollSeconds;
			Poll = saved.Poll;
			RequireFingerprint = _configuration.AppLockEnabled;

			// The poll interval and whether to poll at all are both read
			// when the loop starts, so a change to either only takes
			// effect on a restart of it. Stopping and starting is also
			// what turns the loop off: StartAsync declines to run one
			// when polling is disabled.
			await _alerts.StopAsync().ConfigureAwait(false);
			await _alerts.StartAsync().ConfigureAwait(false);

			StatusMessage = lockRefusal ?? "Saved.";
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Settings could not be saved");
			StatusMessage = "Could not save those settings.";
		}
	}

	/// <summary>
	/// Put Hand out of the way without taking the handset off duty.
	///
	/// <para>The button this sits behind exists because the two things a
	/// responder might mean by "close this" have opposite consequences,
	/// and only one of them is on this page by default. Sign out ends the
	/// shift; this ends nothing. See <see cref="IWindowVisibility"/>.</para>
	/// </summary>
	[RelayCommand]
	private void Hide()
	{
		// No status message: the app is about to stop being on screen, so
		// there would be nobody to read it.
		_window.Hide();
	}

	[RelayCommand]
	private async Task SignOutAsync()
	{
		try
		{
			// Asked before anything happens, and worded as what it costs
			// rather than as what it is. Signing out is the one control on
			// this page that silences the handset for the rest of the shift,
			// it cannot be undone without a working sign-in, and nothing
			// afterwards will point out that the phone has gone quiet — so
			// the responder is told here, while it is still their choice.
			var confirmed = await MainThread.InvokeOnMainThreadAsync(
				() => Shell.Current.DisplayAlertAsync(
					"Sign out and stop alerts?",
					"This handset comes off the rota. It will stop receiving helpline alerts — it will not ring, and nothing will tell you it has gone quiet. You will need to sign in again to put it back on duty.",
					"Sign out",
					"Stay on duty")).ConfigureAwait(false);

			if (!confirmed)
			{
				StatusMessage = "Still signed in.";
				return;
			}

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
