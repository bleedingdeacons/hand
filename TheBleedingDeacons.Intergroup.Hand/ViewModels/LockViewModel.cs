using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.ViewModels;

/// <summary>
/// The lock screen: one fingerprint between a launch and the duty screen.
///
/// <para><b>An alert opens this screen from the inside.</b> The alert
/// loop is running behind the lock — it was started before the gate, and
/// polling, push and the alarm all carry on regardless — so the moment
/// anything becomes outstanding, this view-model stops asking and lets
/// the responder through. Nothing here is allowed to be the reason an
/// alert went unacknowledged, which is also why <c>App</c> skips the
/// screen entirely when something is already waiting.</para>
///
/// <para><b>Sign out is the way out, and it is not a hole.</b> A
/// fingerprint that will not take on a handset that still has a working
/// sensor would otherwise be unrecoverable, and a bricked duty phone is
/// a worse outcome than the one this screen guards against. Whoever
/// takes it clears the token and lands on sign-in, where Reach asks who
/// they are — so the escape hatch costs the enrolment and reveals
/// nothing.</para>
/// </summary>
public sealed partial class LockViewModel : ObservableObject
{
	private readonly IAppLock _lock;
	private readonly IAlertService _alerts;
	private readonly IDeviceAuthService _auth;

	/// <summary>
	/// Guards the one-way trip to the duty screen. Both routes out of here
	/// can fire at once — a fingerprint accepted at the same moment a poll
	/// finds an alert — and two overlapping shell navigations is a race
	/// worth not having.
	/// </summary>
	private bool _opened;

	public LockViewModel(IAppLock appLock, IAlertService alerts, IDeviceAuthService auth)
	{
		_lock = appLock ?? throw new ArgumentNullException(nameof(appLock));
		_alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
		_auth = auth ?? throw new ArgumentNullException(nameof(auth));
	}

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	/// <summary>
	/// Start watching for alerts. Called when the page appears; paired
	/// with <see cref="Detach"/> so a handset that unlocks and comes back
	/// hours later is not listening twice.
	/// </summary>
	public void Attach()
	{
		_alerts.Active.CollectionChanged += OnAlertsChanged;

		// Between App deciding to lock and this page appearing, a poll may
		// already have landed something.
		if (_alerts.Active.Count > 0)
		{
			_ = OpenAsync();
		}
	}

	public void Detach() => _alerts.Active.CollectionChanged -= OnAlertsChanged;

	[RelayCommand]
	private async Task UnlockAsync()
	{
		if (IsBusy || _opened)
		{
			return;
		}

		IsBusy = true;
		StatusMessage = string.Empty;

		try
		{
			// ConfigureAwait(true), deliberately, where the rest of this app
			// uses false: the command is started from OnAppearing, so keeping
			// the UI context means IsBusy and StatusMessage below are set on
			// the thread that owns the views they are bound to.
			var result = await _lock.AuthenticateAsync("Unlock Hand").ConfigureAwait(true);

			switch (result)
			{
				case AppLockResult.Unlocked:
				case AppLockResult.Unavailable:
					// Unavailable opens the app. See IAppLock: a sensor that
					// has stopped working must not take a certified responder
					// off the rota.
					if (result == AppLockResult.Unavailable)
					{
						Log.Warning("Fingerprint could not be asked for; opening the app unlocked");
					}

					await OpenAsync().ConfigureAwait(false);
					break;

				default:
					StatusMessage = "Not unlocked. Touch Unlock to try again.";
					break;
			}
		}
		catch (Exception ex)
		{
			// A thrown lock is an unavailable lock, and unavailable opens.
			Log.Error(ex, "The fingerprint prompt failed; opening the app unlocked");
			await OpenAsync().ConfigureAwait(false);
		}
		finally
		{
			IsBusy = false;
		}
	}

	[RelayCommand]
	private async Task SignOutAsync()
	{
		try
		{
			_opened = true;

			await _alerts.StopAsync().ConfigureAwait(false);
			await _auth.SignOutAsync().ConfigureAwait(false);

			await MainThread.InvokeOnMainThreadAsync(
				() => Shell.Current.GoToAsync("//signin")).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Sign-out from the lock screen failed");
			_opened = false;
			StatusMessage = "Could not sign out.";
		}
	}

	private void OnAlertsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (_alerts.Active.Count > 0)
		{
			Log.Information("An alert arrived while the handset was locked; opening the duty screen");
			_ = OpenAsync();
		}
	}

	private async Task OpenAsync()
	{
		if (_opened)
		{
			return;
		}

		_opened = true;
		Detach();

		await MainThread.InvokeOnMainThreadAsync(
			() => Shell.Current.GoToAsync("//alerts")).ConfigureAwait(false);
	}
}
