using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.ViewModels;

/// <summary>
/// Signing a responder in, by provider or by password.
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
	private readonly IDeviceAuthService _auth;
	private readonly IAlertService _alerts;
	private readonly IPlatformAlertPresenter _presenter;
	private readonly IConfigurationService _configuration;

	public SignInViewModel(
		IDeviceAuthService auth,
		IAlertService alerts,
		IPlatformAlertPresenter presenter,
		IConfigurationService configuration)
	{
		_auth = auth;
		_alerts = alerts;
		_presenter = presenter;
		_configuration = configuration;
	}

	[ObservableProperty]
	public partial string Email { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string Password { get; set; } = string.Empty;

	[ObservableProperty]
	public partial string ErrorMessage { get; set; } = string.Empty;

	[ObservableProperty]
	public partial bool IsBusy { get; set; }

	/// <summary>
	/// Shown when the handset cannot post notifications. Not fatal — the
	/// app still alarms while it is open — but it does mean the thing a
	/// responder is relying on will not happen with the app closed, so it
	/// is said plainly rather than left to be discovered at 3am.
	/// </summary>
	[ObservableProperty]
	public partial string PermissionWarning { get; set; } = string.Empty;

	public bool HasError => ErrorMessage.Length > 0;

	public bool HasPermissionWarning => PermissionWarning.Length > 0;

	partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

	partial void OnPermissionWarningChanged(string value) => OnPropertyChanged(nameof(HasPermissionWarning));

	/// <summary>Whether Reach's address has been configured at all.</summary>
	public bool IsConfigured => _configuration.GetReachConfiguration().IsValid();

	[RelayCommand]
	private async Task SignInWithProviderAsync(string provider)
	{
		if (IsBusy)
		{
			return;
		}

		await RunAsync(() => _auth.SignInWithSsoAsync(provider)).ConfigureAwait(false);
	}

	[RelayCommand]
	private async Task SignInWithPasswordAsync()
	{
		if (IsBusy)
		{
			return;
		}

		if (Email.Trim().Length == 0 || Password.Length == 0)
		{
			ErrorMessage = "Enter your email address and password.";
			return;
		}

		await RunAsync(() => _auth.SignInWithPasswordAsync(Email.Trim(), Password)).ConfigureAwait(false);
	}

	private async Task RunAsync(Func<Task<ReachResult<DeviceSession>>> signIn)
	{
		IsBusy = true;
		ErrorMessage = string.Empty;

		try
		{
			var result = await signIn().ConfigureAwait(false);

			if (!result.Success)
			{
				// A cancelled browser sheet reports no failure and no
				// message; the responder closed it deliberately and does not
				// need to be told what they just did.
				ErrorMessage = result.Failure == ReachFailure.None ? string.Empty : result.Message;
				return;
			}

			Password = string.Empty;

			// Ask for notification permission only after a successful sign-in.
			// The prompt makes sense once a responder has committed to using
			// the app, and iOS gives exactly one chance to ask.
			//
			// Guarded separately, and this matters more than it looks. By this
			// point enrolment has already succeeded on the server: the handset
			// exists, the token is stored, and the responder is signed in. If
			// anything here threw it would fall into the catch below and be
			// reported as "something went wrong signing in" — leaving them on
			// this screen, believing they had failed, tapping again and
			// enrolling a second handset against the same person. Whether the
			// permission prompt worked has no bearing on whether sign-in did.
			try
			{
				if (!await _presenter.RequestPermissionsAsync().ConfigureAwait(false))
				{
					PermissionWarning =
						"Notifications are turned off for Hand. Alerts will only sound while the app is open — "
						+ "turn notifications on in your device settings so you are alerted when it is closed.";
				}
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Notification permission could not be requested");
				PermissionWarning =
					"Hand could not check its notification permission. Alerts may only sound while the app is "
					+ "open — check notifications are turned on for Hand in your device settings.";
			}

			await _alerts.StartAsync().ConfigureAwait(false);

			await MainThread.InvokeOnMainThreadAsync(
				() => Shell.Current.GoToAsync("//alerts")).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Sign-in failed unexpectedly");
			ErrorMessage = "Something went wrong signing in. Please try again.";
		}
		finally
		{
			IsBusy = false;
		}
	}
}
