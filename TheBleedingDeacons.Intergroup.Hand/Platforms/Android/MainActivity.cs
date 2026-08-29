using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
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

		ApplySystemBarInsets();
	}

	/// <summary>
	/// Keep the app's content out from under the status and navigation
	/// bars.
	///
	/// <para><b>Android draws behind them now whether we ask or not.</b>
	/// Android 15 began enforcing edge-to-edge for apps targeting SDK 35+
	/// and Android 16 removed the opt-out, so the window fills the display
	/// and the system bars are painted over the top of it. Without this
	/// the duty header renders underneath the clock — which is the line a
	/// responder reads first, and the one that says whether anything is
	/// waiting.</para>
	///
	/// <para><b>Here rather than in the XAML.</b> MAUI's
	/// <c>SafeAreaEdges</c> compiles on Android but does not inset
	/// anything on it, and a per-page property would in any case be four
	/// places to forget — every page this app has draws its own root
	/// layout. Padding the single content view covers all of them at
	/// once, and any page added later.</para>
	///
	/// <para>Read from the live insets rather than from a measured status
	/// bar height: the two differ on handsets with a cutout, and the
	/// listener also re-runs when they change — a call in progress, or a
	/// switch between gesture and button navigation, both resize the
	/// bars under a running app.</para>
	/// </summary>
	private void ApplySystemBarInsets()
	{
		var content = FindViewById(Android.Resource.Id.Content);
		if (content is null)
		{
			return;
		}

		ViewCompat.SetOnApplyWindowInsetsListener(content, new SystemBarInsetListener());
	}

	/// <summary>
	/// Pads the content view by whatever the system bars currently
	/// occupy. See <see cref="ApplySystemBarInsets"/>.
	///
	/// <para>The insets are returned rather than consumed, so anything
	/// else that wants to know about them still hears.</para>
	/// </summary>
	private sealed class SystemBarInsetListener : Java.Lang.Object, IOnApplyWindowInsetsListener
	{
		public WindowInsetsCompat OnApplyWindowInsets(
			Android.Views.View view,
			WindowInsetsCompat insets)
		{
			ArgumentNullException.ThrowIfNull(view);
			ArgumentNullException.ThrowIfNull(insets);

			// System bars and the display cutout together: a punch-hole or
			// notch is not part of the status bar inset, and a handset held
			// in a case that covers one still must not lose its header.
			var bars = insets.GetInsets(
				WindowInsetsCompat.Type.SystemBars() | WindowInsetsCompat.Type.DisplayCutout());

			view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);

			return insets;
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
