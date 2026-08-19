using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand;

public partial class App : Application
{
	private readonly IDeviceAuthService _auth;
	private readonly IAlertService _alerts;
	private readonly IConfigurationService _configuration;

	public App(IDeviceAuthService auth, IAlertService alerts, IConfigurationService configuration)
	{
		InitializeComponent();

		_auth = auth;
		_alerts = alerts;
		_configuration = configuration;

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
				await GoAsync("//alerts").ConfigureAwait(false);
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
				await GoAsync("//alerts").ConfigureAwait(false);
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
