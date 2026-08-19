using TheBleedingDeacons.Intergroup.Hand.ViewModels;

namespace TheBleedingDeacons.Intergroup.Hand.Views;

public partial class AlertsPage : ContentPage
{
	private readonly AlertsViewModel _viewModel;

	/// <summary>
	/// The view-model, typed, for bindings inside the alert DataTemplate.
	/// </summary>
	/// <remarks>
	/// <para>Inside a DataTemplate the binding context is the
	/// <c>HandAlert</c>, not the page, so a button that invokes a command
	/// on the view-model has to reach back out. The obvious way to write
	/// that — <c>RelativeSource AncestorType={x:Type vm:AlertsViewModel}</c>
	/// — does not resolve inside a CollectionView's template, and it fails
	/// the worst way possible: silently. The binding simply never produces
	/// a command, so the button renders, is enabled, depresses when
	/// tapped, and does nothing at all. Acknowledge all kept working
	/// throughout because it sits outside the template and binds to the
	/// page's own context.</para>
	///
	/// <para>Binding through <c>{x:Reference}</c> to this property is the
	/// version the XAML compiler can actually check: the source is this
	/// page, the path is a typed property, so a rename or a typo becomes a
	/// build error (XC0045) rather than another dead button.</para>
	/// </remarks>
	public AlertsViewModel ViewModel => _viewModel;

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
