using TheBleedingDeacons.Intergroup.Hand.ViewModels;

namespace TheBleedingDeacons.Intergroup.Hand.Views;

public partial class SettingsPage : ContentPage
{
	private readonly SettingsViewModel _viewModel;

	public SettingsPage(SettingsViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Ask the platform what it can do, every time the page is opened.
	///
	/// <para>A responder who has just gone to the phone's own settings to
	/// enrol a fingerprint comes back here expecting the checkbox to work,
	/// and an answer cached at construction would still say it cannot.</para>
	/// </summary>
	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Fire and forget: OnAppearing cannot await, and LoadAsync owns its
		// own errors.
		_ = _viewModel.LoadAsync();
	}
}
