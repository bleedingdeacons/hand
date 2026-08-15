using TheBleedingDeacons.Intergroup.Hand.Views;

namespace TheBleedingDeacons.Intergroup.Hand;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Settings is pushed rather than being a top-level tab: it is
		// reached from the duty screen and should be a page you come back
		// from, not somewhere the app can be left sitting.
		Routing.RegisterRoute("settings", typeof(SettingsPage));
	}
}
