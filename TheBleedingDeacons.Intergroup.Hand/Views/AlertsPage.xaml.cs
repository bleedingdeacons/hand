using TheBleedingDeacons.Intergroup.Hand.ViewModels;

namespace TheBleedingDeacons.Intergroup.Hand.Views;

public partial class AlertsPage : ContentPage
{
	private readonly AlertsViewModel _viewModel;

	public AlertsPage(AlertsViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// The responder name and the duty state can both have changed while
		// this page was off screen — a sign-in happened, or settings were
		// saved — and neither raises a change notification of its own.
		_viewModel.Refresh();
	}
}
