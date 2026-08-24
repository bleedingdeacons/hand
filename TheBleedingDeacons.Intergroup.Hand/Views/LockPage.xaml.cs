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
	/// <remarks>
	/// <para><b>Dispatched, not called.</b> OnAppearing runs inside Shell's
	/// own fragment transaction - the stack goes straight through
	/// ShellSectionRenderer.onCreateView - and AndroidX BiometricPrompt
	/// adds a fragment of its own and then calls
	/// executePendingTransactions, which throws "FragmentManager is already
	/// executing transactions" when it lands inside one. Calling it here
	/// directly therefore threw every time, and the failure was the quiet
	/// kind: an unraisable prompt reads as an unavailable sensor, which
	/// opens the handset, so the lock simply did not happen and nothing on
	/// screen said so.</para>
	///
	/// <para>Dispatching puts it on the next turn of the main loop, by
	/// which time the transaction that is running this method has
	/// finished. AppLock retries once as well; between them the prompt has
	/// to be genuinely unavailable to be reported as such.</para>
	/// </remarks>
	protected override void OnAppearing()
	{
		base.OnAppearing();

		_viewModel.Attach();

		// Fire and forget on purpose: OnAppearing cannot await, and the
		// command owns its own errors.
		Dispatcher.Dispatch(() => _ = _viewModel.UnlockCommand.ExecuteAsync(null));
	}

	protected override void OnDisappearing()
	{
		_viewModel.Detach();

		base.OnDisappearing();
	}
}
