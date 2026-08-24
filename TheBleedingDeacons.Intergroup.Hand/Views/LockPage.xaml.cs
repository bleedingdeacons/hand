using TheBleedingDeacons.Intergroup.Hand.ViewModels;

namespace TheBleedingDeacons.Intergroup.Hand.Views;

public partial class LockPage : ContentPage
{
	private readonly LockViewModel _viewModel;

	public LockPage(LockViewModel viewModel)
	{
		InitializeComponent();

		_viewModel = viewModel;
		BindingContext = viewModel;
	}

	/// <summary>
	/// Raise the prompt as soon as the page is up, rather than making a
	/// responder press Unlock to be asked to unlock. The button is there
	/// for the second attempt, and for the case where the prompt was
	/// dismissed by something else entirely - a call arriving, the screen
	/// timing out.
	/// </summary>
	protected override void OnAppearing()
	{
		base.OnAppearing();

		_viewModel.Attach();

		// Fire and forget on purpose: OnAppearing cannot await, and the
		// command owns its own errors.
		_ = _viewModel.UnlockCommand.ExecuteAsync(null);
	}

	protected override void OnDisappearing()
	{
		_viewModel.Detach();

		base.OnDisappearing();
	}
}
