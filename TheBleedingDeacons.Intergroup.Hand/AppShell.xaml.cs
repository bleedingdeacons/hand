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

		// History likewise: somewhere you go, look at, and come back from.
		// It is emphatically not a place a duty handset should be left
		// sitting — the alerts page is.
		Routing.RegisterRoute("history", typeof(HistoryPage));

		// Writing a message. Pushed for the same reason as the other two,
		// and more so: a half-typed message left on screen is a handset
		// that is not showing the alert it just received.
		Routing.RegisterRoute("compose", typeof(ComposePage));
	}
}
