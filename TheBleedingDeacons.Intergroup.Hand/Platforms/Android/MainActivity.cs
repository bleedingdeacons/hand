using Android.App;
using Android.Content.PM;
using Android.OS;
using TheBleedingDeacons.Intergroup.Hand.Services;

namespace TheBleedingDeacons.Intergroup.Hand;

/// <summary>
/// The single activity.
///
/// <para><c>LaunchMode.SingleTop</c> matters here: the full-screen intent
/// and the notification tap both target this activity, and without it
/// each would stack another copy on top of the last, so a responder
/// dismissing an alert would find another Hand behind it.</para>
///
/// <para><c>ShowWhenLocked</c> and <c>TurnScreenOn</c> are what let the
/// full-screen intent do its job — display over the lock screen and wake
/// the display — rather than quietly opening behind it.</para>
/// </summary>
[Activity(
	Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	LaunchMode = LaunchMode.SingleTop,
	ScreenOrientation = ScreenOrientation.Portrait,
	ConfigurationChanges = ConfigChanges.ScreenSize
		| ConfigChanges.Orientation
		| ConfigChanges.UiMode
		| ConfigChanges.ScreenLayout
		| ConfigChanges.SmallestScreenSize
		| ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		// Created here as well as before the first notification, because a
		// full-screen intent arriving from a cold start must find the
		// channel already configured — a channel created after the fact
		// would have the wrong sound for that first alert.
		PlatformAlertPresenter.EnsureChannel(this);

		// The background poll, which is what alerts a handset whose process
		// is not running and whose push has failed or was never available.
		// Scheduled at every launch rather than at sign-in: the policy is
		// Keep, so an existing schedule is left alone, and the work stops at
		// its own token check until there is something to poll for.
		Platforms.Android.HandPollWorker.Schedule(this);

		if (OperatingSystem.IsAndroidVersionAtLeast(27))
		{
			SetShowWhenLocked(true);
			SetTurnScreenOn(true);
		}
	}

	/// <summary>
	/// Ship the log buffer when the app leaves the foreground.
	/// </summary>
	/// <remarks>
	/// <para>This is the last moment Android reliably gives us. A
	/// backgrounded app can be killed at any time for memory, by an OEM
	/// battery manager, or by the user swiping it away, and none of those
	/// raise <c>Destroying</c> on the MAUI window - so the flush wired
	/// there never runs. On a duty handset, backgrounded is the app's
	/// normal state, which makes this the common path rather than an edge
	/// case.</para>
	///
	/// <para>Non-destructive: the responder is still on duty and the app
	/// may well come back, so the pipeline is flushed and rebuilt rather
	/// than closed. Anything still buffered survives on disk regardless
	/// and ships on the next launch; this is about arriving tonight
	/// instead of whenever the app is next opened.</para>
	/// </remarks>
	protected override void OnStop()
	{
		try
		{
			Services.BetterStackLoggerController.Current?.Flush();
		}
		catch
		{
			// Never let logging interfere with the activity lifecycle.
		}

		base.OnStop();
	}
}
