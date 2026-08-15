using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Signing a handset in, and keeping its enrolment current.
///
/// <para>The SSO path runs Reach's ordinary OAuth flow in the system
/// browser via <see cref="WebAuthenticator"/> — ASWebAuthenticationSession
/// on Apple platforms, Custom Tabs on Android — and catches the redirect
/// back to Hand's own URI scheme. What comes back is a one-time code, not
/// a token: the redirect passes through the browser, where it lands in
/// history and can be read by anything else registered for the scheme, so
/// the code is traded for the real credential over TLS in a direct POST.
/// That is RFC 8252, and the reason it exists.</para>
///
/// <para>The password path skips all of that. It is what the Windows head
/// uses when it is not packaged as MSIX and so cannot claim a custom
/// scheme, and it is the fallback anywhere the browser round trip
/// fails.</para>
/// </summary>
public sealed class DeviceAuthService : IDeviceAuthService
{
	private readonly IReachClient _reach;
	private readonly IConfigurationService _configuration;
	private readonly IPushRegistrar _push;

	public DeviceAuthService(
		IReachClient reach,
		IConfigurationService configuration,
		IPushRegistrar push)
	{
		_reach = reach ?? throw new ArgumentNullException(nameof(reach));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
		_push = push ?? throw new ArgumentNullException(nameof(push));
	}

	public DeviceSession? Current { get; private set; }

	public bool IsSignedIn => Current is not null;

	public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
	{
		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			return false;
		}

		var result = await _reach.GetSessionAsync(token, cancellationToken).ConfigureAwait(false);

		if (result.Success && result.Value is not null)
		{
			Current = result.Value;

			// Re-register the push token on every launch. Firebase rotates
			// them silently, and a handset holding a stale one looks enrolled
			// while receiving nothing — the exact failure this app cannot
			// have. Cheap enough to do unconditionally.
			await ReRegisterPushAsync(token, cancellationToken).ConfigureAwait(false);

			return true;
		}

		if (result.Failure is ReachFailure.Unauthenticated or ReachFailure.NotEligible)
		{
			// Revoked, or the responder is no longer certified. The token is
			// dead and keeping it would only produce 401s forever.
			Log.Information("Stored device token is no longer accepted ({Failure}); clearing", result.Failure);
			await _configuration.ClearDeviceTokenAsync().ConfigureAwait(false);
			Current = null;
			return false;
		}

		// Offline. The token may well still be good, so it is kept — a
		// responder whose broadband is down must not be signed out — but we
		// cannot claim a session we have not confirmed.
		Log.Debug("Session could not be confirmed ({Failure}); keeping the stored token", result.Failure);
		return false;
	}

	public async Task<ReachResult<DeviceSession>> SignInWithSsoAsync(
		string provider, CancellationToken cancellationToken = default)
	{
		string code;

		try
		{
			var (start, callback) = _reach.BuildSignInUrls(provider);

			var authResult = await WebAuthenticator.Default
				.AuthenticateAsync(start, callback)
				.ConfigureAwait(false);

			// Reach sends back either ?code= on success or ?error= with a
			// slug describing the refusal. Sending the refusal to the app
			// rather than rendering a page matters: a friendly page inside an
			// in-app browser tab is a dead end the app never hears about, so
			// it would hang on the sign-in sheet until the responder gave up.
			if (authResult.Properties.TryGetValue("error", out var error))
			{
				return ReachResult<DeviceSession>.Fail(
					error == "not_eligible" ? ReachFailure.NotEligible : ReachFailure.Server,
					DescribeError(error));
			}

			if (!authResult.Properties.TryGetValue("code", out var returned) || string.IsNullOrEmpty(returned))
			{
				return ReachResult<DeviceSession>.Fail(
					ReachFailure.Server, "Sign-in did not complete. Please try again.");
			}

			code = returned;
		}
		catch (TaskCanceledException)
		{
			// The responder closed the sheet. Not an error to report back.
			return ReachResult<DeviceSession>.Fail(ReachFailure.None, string.Empty);
		}
		catch (InvalidOperationException ex)
		{
			// Thrown by the client when the server address is unset.
			return ReachResult<DeviceSession>.Fail(ReachFailure.NotConfigured, ex.Message);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Browser sign-in failed");
			return ReachResult<DeviceSession>.Fail(
				ReachFailure.Server, "Sign-in could not be completed on this device.");
		}

		var (pushProvider, pushToken) = await PushDetailsAsync().ConfigureAwait(false);

		var result = await _reach.ExchangeCodeAsync(
			code,
			_configuration.DeviceLabel,
			PlatformName(),
			pushProvider,
			pushToken,
			cancellationToken).ConfigureAwait(false);

		return await StoreAsync(result).ConfigureAwait(false);
	}

	public async Task<ReachResult<DeviceSession>> SignInWithPasswordAsync(
		string email, string password, CancellationToken cancellationToken = default)
	{
		var (pushProvider, pushToken) = await PushDetailsAsync().ConfigureAwait(false);

		var result = await _reach.SignInWithPasswordAsync(
			email,
			password,
			_configuration.DeviceLabel,
			PlatformName(),
			pushProvider,
			pushToken,
			cancellationToken).ConfigureAwait(false);

		return await StoreAsync(result).ConfigureAwait(false);
	}

	public async Task SignOutAsync(CancellationToken cancellationToken = default)
	{
		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);

		if (!string.IsNullOrEmpty(token))
		{
			// Best effort. A handset that cannot reach the server still signs
			// out locally — the responder asked to — and the server-side row
			// is left for an admin to revoke from the Devices page.
			var result = await _reach.SignOutAsync(token, cancellationToken).ConfigureAwait(false);
			if (!result.Success)
			{
				Log.Warning("Sign-out could not be recorded on the server: {Message}", result.Message);
			}
		}

		await _configuration.ClearDeviceTokenAsync().ConfigureAwait(false);
		Current = null;
	}

	public async Task RegisterPushTokenAsync(string pushToken, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(pushToken))
		{
			return;
		}

		var token = await _configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			// Not enrolled yet; the token will be sent as part of enrolment.
			return;
		}

		var result = await _reach
			.UpdatePushTokenAsync(token, _push.Provider, pushToken, cancellationToken)
			.ConfigureAwait(false);

		if (result.Success)
		{
			Log.Information("Push registration token updated with Reach");
		}
		else
		{
			Log.Warning("Push registration token could not be updated: {Message}", result.Message);
		}
	}

	private async Task ReRegisterPushAsync(string deviceToken, CancellationToken cancellationToken)
	{
		var (provider, pushToken) = await PushDetailsAsync().ConfigureAwait(false);
		if (provider.Length == 0 || pushToken.Length == 0)
		{
			return;
		}

		await _reach.UpdatePushTokenAsync(deviceToken, provider, pushToken, cancellationToken)
			.ConfigureAwait(false);
	}

	private async Task<ReachResult<DeviceSession>> StoreAsync(ReachResult<DeviceSession> result)
	{
		if (!result.Success || result.Value is null)
		{
			return result;
		}

		await _configuration.SaveDeviceTokenAsync(result.Value.Token).ConfigureAwait(false);

		// Keep the session but drop the plaintext token from memory — it is
		// in secure storage now, and nothing above this needs it.
		result.Value.Token = string.Empty;
		Current = result.Value;

		Log.Information(
			"Handset enrolled for {Responder} on {Platform} (push: {Push})",
			result.Value.Responder,
			result.Value.Platform,
			string.IsNullOrEmpty(result.Value.PushProvider) ? "poll only" : result.Value.PushProvider);

		return result;
	}

	private async Task<(string Provider, string Token)> PushDetailsAsync()
	{
		var provider = _push.Provider;
		if (provider.Length == 0)
		{
			return (string.Empty, string.Empty);
		}

		var token = await _push.GetTokenAsync().ConfigureAwait(false);

		// A transport with no token would enrol a handset that expects push
		// and never gets it. Reporting no transport is honest, and the poll
		// still covers it; the token is registered later if it turns up.
		return token.Length == 0 ? (string.Empty, string.Empty) : (provider, token);
	}

	/// <summary>
	/// The platform name Reach expects. Must match
	/// <c>Device::PLATFORMS</c> on the server, which refuses anything else
	/// rather than guessing — the platform decides the delivery path.
	/// </summary>
	private static string PlatformName()
	{
		var platform = DeviceInfo.Platform;

		if (platform == DevicePlatform.Android)
		{
			return "android";
		}

		if (platform == DevicePlatform.iOS)
		{
			return "ios";
		}

		if (platform == DevicePlatform.MacCatalyst)
		{
			return "maccatalyst";
		}

		return "windows";
	}

	private static string DescribeError(string slug) => slug switch
	{
		"not_eligible" =>
			"Hand is for certified telephone responders. Please contact your intergroup if you believe this is in error.",
		"email_required" =>
			"That provider didn’t share a usable email address. Please sign in again and choose to share it, or use a different provider.",
		_ => "Sign-in failed. Please try again.",
	};
}
