using TheBleedingDeacons.Intergroup.Hand.ViewModels;

namespace TheBleedingDeacons.Intergroup.Hand.Views;

public partial class HistoryPage : ContentPage
{
	public HistoryPage(HistoryViewModel viewModel)
	{
		InitializeComponent();

		ViewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Exposed so a row's tap gesture can reach the command. Each row's
	/// binding context is its own entry, not the page's.
	/// </summary>
	public HistoryViewModel ViewModel { get; }

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Fire and forget: OnAppearing cannot await, and LoadAsync owns
		// its own errors.
		_ = ViewModel.LoadAsync();
	}

	/// <summary>
	/// Confirm before forgetting.
	///
	/// <para>In the page rather than the view model because the prompt is
	/// MAUI's, and the view model is the half that has tests. What it
	/// guards is not recoverable: there is no second copy of this
	/// anywhere, and Reach purges its own alerts an hour after they
	/// expire.</para>
	/// </summary>
	private async void OnClearClicked(object? sender, EventArgs e)
	{
		try
		{
			var confirmed = await DisplayAlert(
				"Clear history?",
				"This forgets every alert this handset has recorded. It cannot be undone.",
				"Clear",
				"Keep");

			if (confirmed)
			{
				await ViewModel.ClearCommand.ExecuteAsync(null);
			}
		}
		catch (Exception ex)
		{
			Serilog.Log.Warning(ex, "Clearing the alert history failed");
		}
	}
}
