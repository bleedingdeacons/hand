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

		if (OperatingSystem.IsAndroidVersionAtLeast(27))
		{
			SetShowWhenLocked(true);
			SetTurnScreenOn(true);
		}
	}
}
