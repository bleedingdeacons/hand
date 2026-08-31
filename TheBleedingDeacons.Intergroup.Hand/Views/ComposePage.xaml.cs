using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.ViewModels;

namespace TheBleedingDeacons.Intergroup.Hand.Views;

public partial class ComposePage : ContentPage
{
	public ComposePage(ComposeViewModel viewModel)
	{
		InitializeComponent();

		ViewModel = viewModel;
		BindingContext = viewModel;
	}

	public ComposeViewModel ViewModel { get; }

	protected override void OnAppearing()
	{
		base.OnAppearing();

		// Fire and forget: OnAppearing cannot await, and LoadAsync reports
		// its own failures into Status rather than throwing.
		_ = ViewModel.LoadAsync();
	}

	/// <summary>
	/// The level radios, written one-way in XAML and set from here.
	///
	/// <para>A two-way binding on <c>IsChecked</c> would fight itself:
	/// selecting one radio unchecks the other two, and each of those
	/// unchecks fires the same handler. Reading the checked one and
	/// ignoring the rest is what keeps a single set from being three
	/// writes in an order nothing controls.</para>
	/// </summary>
	private void OnLevelChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value && sender is RadioButton { Value: string level })
		{
			ViewModel.Level = level;
		}
	}

	private void OnMemberScopeChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value)
		{
			ViewModel.RecipientMode = 0;
		}
	}

	private void OnCommitteeScopeChecked(object? sender, CheckedChangedEventArgs e)
	{
		if (e.Value)
		{
			ViewModel.RecipientMode = 1;
		}
	}
}
