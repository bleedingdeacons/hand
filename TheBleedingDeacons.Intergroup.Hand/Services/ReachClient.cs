using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Talks to Reach's REST API over the platform-native HttpClient.
///
/// <para>Every method returns a <see cref="ReachResult{T}"/> rather than
/// throwing. Failure here is ordinary — a handset on a train loses signal
/// several times a journey — and the difference between "try again in
/// twenty seconds" and "your certification has lapsed" is exactly what
/// the caller needs to act on, which an exception type would flatten.</para>
///
/// <para>Reach's error bodies are WordPress's shape:
/// <c>{"code":"reach_not_eligible","message":"…","data":{"status":403}}</c>.
/// The code is what gets mapped, not the status, because several distinct
/// refusals share a status and the app treats them differently.</para>
/// </summary>
public sealed class ReachClient : IReachClient
{
	/// <summary>
	/// The URI scheme Hand registers on Android, iOS and macOS. Must match
	/// <c>DeviceRedirectValidator::APP_SCHEME</c> on the server and the
	/// platform manifests — it is a contract between the three, not a
	/// setting.
	/// </summary>
	public const string CallbackScheme = "hand";

	public const string CallbackUri = "hand://auth";

	private const string ApiRoot = "wp-json/reach/v1/";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly HttpClient _httpClient;
	private readonly IConfigurationService _configuration;

	public ReachClient(HttpClient httpClient, IConfigurationService configuration)
	{
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	public (Uri Start, Uri Callback) BuildSignInUrls(string provider)
	{
		var callback = new Uri(CallbackUri);
		var start = new Uri(
			Endpoint("auth/device/start"),
			// The redirect is sent so the server can validate it against its
			// allow-list and stash it with the OAuth state. It is never echoed
			// back from the callback, which is what stops it being a redirect
			// an attacker can influence.
			$"?provider={Uri.EscapeDataString(provider)}&redirect_uri={Uri.EscapeDataString(CallbackUri)}");

		return (start, callback);
	}

	public Task<ReachResult<DeviceSession>> ExchangeCodeAsync(
		string code, string label, string platform, string pushProvider, string pushToken, CancellationToken cancellationToken)
	{
		return PostAsync<DeviceSession>(
			"auth/device/exchange",
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["code"] = code,
				["label"] = label,
				["platform"] = platform,
				["push_provider"] = pushProvider,
				["push_token"] = pushToken,
			},
			token: null,
			cancellationToken);
	}

	public Task<ReachResult<DeviceSession>> SignInWithPasswordAsync(
		string email, string password, string label, string platform, string pushProvider, string pushToken, CancellationToken cancellationToken)
	{
		return PostAsync<DeviceSession>(
			"auth/device/password",
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["email"] = email,
				["password"] = password,
				["label"] = label,
				["platform"] = platform,
				["push_provider"] = pushProvider,
				["push_token"] = pushToken,
			},
			token: null,
			cancellationToken);
	}

	public async Task<ReachResult<DeviceSession>> GetSessionAsync(string token, CancellationToken cancellationToken)
	{
		return await SendAsync<DeviceSession>(
			HttpMethod.Get, "auth/device/session", body: null, token, cancellationToken).ConfigureAwait(false);
	}

	public async Task<ReachResult<bool>> UpdatePushTokenAsync(
		string token, string pushProvider, string pushToken, CancellationToken cancellationToken)
	{
		var result = await PostAsync<JsonElement>(
			"auth/device/push",
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["push_provider"] = pushProvider,
				["push_token"] = pushToken,
			},
			token,
			cancellationToken).ConfigureAwait(false);

		return Collapse(result);
	}

	public async Task<ReachResult<bool>> SignOutAsync(string token, CancellationToken cancellationToken)
	{
		var result = await PostAsync<JsonElement>(
			"auth/device/signout",
			new Dictionary<string, string>(StringComparer.Ordinal),
			token,
			cancellationToken).ConfigureAwait(false);

		return Collapse(result);
	}

	public async Task<ReachResult<IReadOnlyList<HandAlert>>> GetPendingAlertsAsync(
		string token, CancellationToken cancellationToken)
	{
		var result = await SendAsync<PendingAlertsResponse>(
			HttpMethod.Get, "alerts", body: null, token, cancellationToken).ConfigureAwait(false);

		if (!result.Success || result.Value is null)
		{
			return ReachResult<IReadOnlyList<HandAlert>>.Fail(result.Failure, result.Message);
		}

		return ReachResult<IReadOnlyList<HandAlert>>.Ok(result.Value.Alerts);
	}

	public async Task<ReachResult<bool>> AcknowledgeAsync(string token, long alertId, CancellationToken cancellationToken)
	{
		var result = await PostAsync<JsonElement>(
			$"alerts/{alertId}/ack",
			new Dictionary<string, string>(StringComparer.Ordinal),
			token,
			cancellationToken).ConfigureAwait(false);

		return Collapse(result);
	}

	public async Task<ReachResult<string>> GetContactAsync(
		string token, long alertId, CancellationToken cancellationToken)
	{
		var result = await SendAsync<ContactResponse>(
			HttpMethod.Get, $"alerts/{alertId}/contact", body: null, token, cancellationToken)
			.ConfigureAwait(false);

		return result.Success && result.Value is not null
			? ReachResult<string>.Ok(result.Value.Contact)
			: ReachResult<string>.Fail(result.Failure, result.Message);
	}

	private Task<ReachResult<T>> PostAsync<T>(
		string path,
		Dictionary<string, string> body,
		string? token,
		CancellationToken cancellationToken)
	{
		return SendAsync<T>(HttpMethod.Post, path, body, token, cancellationToken);
	}

	private async Task<ReachResult<T>> SendAsync<T>(
		HttpMethod method,
		string path,
		Dictionary<string, string>? body,
		string? token,
		CancellationToken cancellationToken)
	{
		Uri endpoint;
		try
		{
			endpoint = Endpoint(path);
		}
		catch (InvalidOperationException ex)
		{
			return ReachResult<T>.Fail(ReachFailure.NotConfigured, ex.Message);
		}

		using var request = new HttpRequestMessage(method, endpoint);

		if (body is not null)
		{
			// Form encoding rather than JSON: WordPress's REST layer reads
			// form fields into request parameters natively, which is what the
			// controllers' registered `args` validation runs against.
			request.Content = new FormUrlEncodedContent(body);
		}

		if (!string.IsNullOrEmpty(token))
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		try
		{
			using var response = await _httpClient
				.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
				.ConfigureAwait(false);

			if (response.IsSuccessStatusCode)
			{
				var value = await response.Content
					.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
					.ConfigureAwait(false);

				return value is null
					? ReachResult<T>.Fail(ReachFailure.Server, "Reach returned an empty response.")
					: ReachResult<T>.Ok(value);
			}

			return await FailureFromAsync<T>(response, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// A deliberate cancellation — the app is closing, or a poll was
			// superseded. Not a failure worth reporting or logging.
			throw;
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
		{
			// Out of signal, DNS failure, timeout. Expected often enough that
			// it is logged at debug: a handset on a train would otherwise fill
			// the log with noise on every poll.
			Log.Debug(ex, "Reach request to {Path} could not be completed", path);
			return ReachResult<T>.Fail(ReachFailure.Network, "Could not reach the server.");
		}
		catch (JsonException ex)
		{
			// A 200 whose body is not what we expected. Usually a hosting
			// interstitial — a WAF challenge page or a maintenance notice —
			// which is worth seeing in the log because it looks like success.
			Log.Warning(ex, "Reach response from {Path} could not be parsed", path);
			return ReachResult<T>.Fail(ReachFailure.Server, "The server sent something unexpected.");
		}
	}

	/// <summary>
	/// Turn a non-2xx response into a typed failure, mapping Reach's own
	/// error code where there is one.
	/// </summary>
	private static async Task<ReachResult<T>> FailureFromAsync<T>(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		var code = string.Empty;
		var message = string.Empty;

		try
		{
			var error = await response.Content
				.ReadFromJsonAsync<WordPressError>(JsonOptions, cancellationToken)
				.ConfigureAwait(false);

			if (error is not null)
			{
				code = error.Code ?? string.Empty;
				message = error.Message ?? string.Empty;
			}
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			// Non-JSON error body — an edge WAF or a proxy, not Reach. The
			// status code below is all we have to go on.
		}

		var failure = code switch
		{
			"reach_not_eligible" => ReachFailure.NotEligible,
			"reach_invalid_credentials" => ReachFailure.InvalidCredentials,
			"reach_device_not_authenticated" => ReachFailure.Unauthenticated,
			"reach_rate_limited" => ReachFailure.RateLimited,
			_ => response.StatusCode switch
			{
				HttpStatusCode.Unauthorized => ReachFailure.Unauthenticated,
				HttpStatusCode.Forbidden => ReachFailure.NotEligible,
				HttpStatusCode.TooManyRequests => ReachFailure.RateLimited,
				_ => ReachFailure.Server,
			},
		};

		if (string.IsNullOrWhiteSpace(message))
		{
			message = $"The server refused the request ({(int)response.StatusCode}).";
		}

		return ReachResult<T>.Fail(failure, message);
	}

	/// <summary>
	/// Reduce a response whose body we do not care about to a plain
	/// success flag, preserving the failure detail.
	/// </summary>
	private static ReachResult<bool> Collapse<T>(ReachResult<T> result) =>
		result.Success
			? ReachResult<bool>.Ok(true)
			: ReachResult<bool>.Fail(result.Failure, result.Message);

	private Uri Endpoint(string path)
	{
		var configuration = _configuration.GetReachConfiguration();
		if (!configuration.IsValid())
		{
			throw new InvalidOperationException(
				"The Reach server address has not been set. Add it in Settings.");
		}

		return new Uri(new Uri(configuration.BaseUrl), ApiRoot + path);
	}

	private sealed class PendingAlertsResponse
	{
		[JsonPropertyName("alerts")]
		public List<HandAlert> Alerts { get; set; } = [];

		[JsonPropertyName("now")]
		public long Now { get; set; }
	}

	private sealed class ContactResponse
	{
		[JsonPropertyName("alert_id")]
		public long AlertId { get; set; }

		[JsonPropertyName("contact")]
		public string Contact { get; set; } = string.Empty;
	}

	private sealed class WordPressError
	{
		[JsonPropertyName("code")]
		public string? Code { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}
}
