using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand;

public partial class App : Application
{
	private readonly IDeviceAuthService _auth;
	private readonly IAlertService _alerts;
	private readonly IConfigurationService _configuration;
	private readonly IAppLock _lock;

	public App(
		IDeviceAuthService auth,
		IAlertService alerts,
		IConfigurationService configuration,
		IAppLock appLock)
	{
		InitializeComponent();

		_auth = auth;
		_alerts = alerts;
		_configuration = configuration;
		_lock = appLock;

		// A handset that loses its authorisation must be told, not merely
		// stopped. Subscribed here rather than on a page so it is caught
		// whatever is on screen — including nothing.
		_alerts.AuthenticationLost += OnAuthenticationLost;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell()) { Title = "Hand" };

		window.Created += (_, _) => _ = StartAsync();

		// Resuming is the moment a handset is most likely to be behind:
		// it may have been asleep, out of signal, or had its poll throttled.
		// One immediate refresh costs a request and closes that gap.
		window.Activated += (_, _) => _ = ResumeAsync();

		window.Stopped += (_, _) => Log.Debug("Window stopped");

		// Flush on close so a session's diagnostics are not lost with it.
		// Anything still buffered ships on the next launch — that is what
		// the durable sink is for.
		window.Destroying += (_, _) => MauiProgram.TryFlushLogs();

		return window;
	}

	/// <summary>
	/// Decide which screen the app opens on, and start listening if it can.
	/// </summary>
	private async Task StartAsync()
	{
		try
		{
			var restored = await _auth.RestoreAsync().ConfigureAwait(false);

			if (restored)
			{
				await _alerts.StartAsync().ConfigureAwait(false);
				await OpenDutyScreenAsync().ConfigureAwait(false);
				return;
			}

			// No usable session. If a token is still stored — the server was
			// simply unreachable — polling still starts, because the token
			// may well be fine and a responder whose broadband is down must
			// not be silently taken off the rota.
			var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
			if (!string.IsNullOrEmpty(token))
			{
				await _alerts.StartAsync().ConfigureAwait(false);
				await OpenDutyScreenAsync().ConfigureAwait(false);
				return;
			}

			await GoAsync("//signin").ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Startup failed; falling back to sign-in");
			await GoAsync("//signin").ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Open the duty screen, or put the lock screen in front of it.
	/// </summary>
	/// <remarks>
	/// <para><b>Cold start is the only moment this is asked.</b> Resuming
	/// does not lock, by design: a duty handset spends its life in the
	/// background and comes forward because something happened, and a
	/// fingerprint on every return would be paid dozens of times a shift
	/// for a phone that has not left its owner's hand.</para>
	///
	/// <para>The alert loop is started before this runs, and deliberately
	/// so — polling, push and the alarm all carry on behind the lock, and
	/// <see cref="ViewModels.LockViewModel"/> steps aside the moment
	/// anything lands.</para>
	/// </remarks>
	private async Task OpenDutyScreenAsync()
	{
		var route = await ShouldLockAsync().ConfigureAwait(false) ? "//lock" : "//alerts";

		await GoAsync(route).ConfigureAwait(false);
	}

	/// <summary>
	/// Whether to ask for a fingerprint before showing the duty screen.
	///
	/// <para>Three ways to answer no, and every one of them opens the app:
	/// the responder has turned the lock off, something is already
	/// outstanding, or this handset cannot ask. The last is what makes the
	/// setting safe to default on — a handset with no fingerprint enrolled
	/// is never asked for one. The second is the interesting one: an alert
	/// waiting at launch means the responder is opening the app
	/// <i>because</i> it rang, and a lock screen at that moment is the app
	/// arguing with its own reason for existing.</para>
	/// </summary>
	private async Task<bool> ShouldLockAsync()
	{
		try
		{
			if (!_configuration.AppLockEnabled)
			{
				return false;
			}

			if (_alerts.Active.Count > 0)
			{
				Log.Information("Alerts are outstanding at launch; the fingerprint lock is skipped");
				return false;
			}

			var available = await _lock.IsAvailableAsync().ConfigureAwait(false);

			if (!available)
			{
				Log.Warning("The fingerprint lock is turned on but this handset cannot ask for one; opening unlocked");
			}

			return available;
		}
		catch (Exception ex)
		{
			// Nothing about a lock may keep a responder out of a duty
			// handset. See IAppLock.
			Log.Error(ex, "The fingerprint lock could not be evaluated; opening unlocked");
			return false;
		}
	}

	private async Task ResumeAsync()
	{
		try
		{
			await _alerts.RefreshAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Refresh on resume failed");
		}
	}

	private static void OnAuthenticationLost(object? sender, AuthenticationLostEventArgs e)
	{
		// The reason travels as a query parameter rather than being pushed
		// into the view model, which is transient — the instance that will
		// be on screen does not exist yet at this point.
		_ = GoAsync($"//signin?reason={Uri.EscapeDataString(e.Reason)}");
	}

	private static async Task GoAsync(string route)
	{
		await MainThread.InvokeOnMainThreadAsync(async () =>
		{
			if (Shell.Current is not null)
			{
				await Shell.Current.GoToAsync(route).ConfigureAwait(false);
			}
		}).ConfigureAwait(false);
	}
}
