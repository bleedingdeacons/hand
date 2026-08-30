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
	/// <summary>
	/// A text field has been finished with, so store what is in it.
	///
	/// <para><b>On leaving rather than on every keystroke.</b> The
	/// address and the poll interval are read by the alert loop, which is
	/// restarted whenever they change — saving per character would restart
	/// it once per letter and would briefly store half a URL as the server
	/// to reach.</para>
	/// </summary>
	private void OnFieldCommitted(object? sender, EventArgs e) => _ = _viewModel.ApplyAsync();

	/// <summary>
	/// Store anything typed and never left.
	///
	/// <para>A responder can type into a field and press Back without it
	/// ever losing focus, and with no Save button that keystroke would
	/// otherwise be the one thing on the page that did not stick.</para>
	/// </summary>
	protected override void OnDisappearing()
	{
		base.OnDisappearing();

		_ = _viewModel.ApplyAsync();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Fire and forget: OnAppearing cannot await, and LoadAsync owns its
		// own errors.
		_ = _viewModel.LoadAsync();
	}
}
