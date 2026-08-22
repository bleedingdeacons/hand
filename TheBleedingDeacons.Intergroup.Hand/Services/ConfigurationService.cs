using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Reads configuration from three places, in order of increasing
/// authority: the embedded <c>appsettings.json</c>, the embedded
/// <c>devsettings.json</c> (dev builds only), and what the user has saved
/// on the device.
///
/// <para>Much smaller than Register's equivalent because Hand has far
/// less to configure — one server address, one log sink, one token — but
/// deliberately the same shape, including the <c>USE_DEV_CREDENTIALS</c>
/// split that keeps real credentials out of production packages.</para>
/// </summary>
public sealed class ConfigurationService : IConfigurationService
{
	private const string DevCredentialsResource =
		"TheBleedingDeacons.Intergroup.Hand.devsettings.json";

	private const string DeviceTokenKey = "hand_device_token";
	/// <summary>
	/// Secure-storage key for the alert payload key.
	///
	/// <para>Internal rather than private because a push can arrive with
	/// no app behind it, and <see cref="Platforms.Android.HeadlessAlerts"/>
	/// then reads secure storage without a container to build this service
	/// from. Sharing the name is what stops the two readers drifting on to
	/// different entries.</para>
	/// </summary>
	internal const string PayloadKeyKey = "hand_payload_key";
	private const string DeviceLabelKey = "device_label";
	private const string ReachBaseUrlKey = "reach_base_url";
	private const string ReachPollSecondsKey = "reach_poll_seconds";
	private const string ReachOnDutyKey = "reach_on_duty";
	private const string ReachPollEnabledKey = "reach_poll_enabled";

	private readonly IConfiguration _configuration;

	private BetterStackConfiguration? _cachedBetterStack;

	public ConfigurationService(IConfiguration configuration)
	{
		_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
	}

	public BetterStackConfiguration GetBetterStackConfiguration()
	{
		if (_cachedBetterStack is not null)
		{
			return _cachedBetterStack;
		}

		var endpoint = ReadSetting("BetterStack", "Endpoint");
		var sourceToken = ReadSetting("BetterStack", "SourceToken");

		// A scheme-less endpoint is given one by the model's setter rather
		// than here, so every path that sets it — this one, the settings
		// page, anything added later — is covered by the same rule. See
		// BetterStackConfiguration.Endpoint for what goes wrong without it.
		_cachedBetterStack = new BetterStackConfiguration
		{
			Endpoint = endpoint,
			SourceToken = sourceToken,
		};

		return _cachedBetterStack;
	}

	public ReachConfiguration GetReachConfiguration()
	{
		// A value saved on the device wins over the built-in default, so
		// one build can be pointed at a staging site without a rebuild.
		var baseUrl = Preferences.Get(ReachBaseUrlKey, string.Empty);
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			baseUrl = ReadSetting("Reach", "BaseUrl");
		}

		var pollSeconds = Preferences.Get(ReachPollSecondsKey, 0);
		if (pollSeconds <= 0
			&& int.TryParse(
				ReadSetting("Reach", "PollSeconds"),
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var configured))
		{
			// Invariant: the value came from a config file written by
			// whoever built the app, not from the device's locale.
			pollSeconds = configured;
		}

		return new ReachConfiguration
		{
			BaseUrl = baseUrl,
			PollSeconds = pollSeconds > 0 ? pollSeconds : 20,
			OnDuty = Preferences.Get(ReachOnDutyKey, true),
			Poll = Preferences.Get(ReachPollEnabledKey, true),
		}.Normalised();
	}

	public Task SaveReachConfigurationAsync(ReachConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var normalised = configuration.Normalised();
		Preferences.Set(ReachBaseUrlKey, normalised.BaseUrl);
		Preferences.Set(ReachPollSecondsKey, normalised.PollSeconds);
		Preferences.Set(ReachOnDutyKey, normalised.OnDuty);
		Preferences.Set(ReachPollEnabledKey, normalised.Poll);

		return Task.CompletedTask;
	}

	public async Task<string> GetPayloadKeyAsync()
	{
		try
		{
			return await SecureStorage.GetAsync(PayloadKeyKey).ConfigureAwait(false) ?? string.Empty;
		}
		catch (Exception ex)
		{
			// Same tolerance as the token below, and the same reasoning: an
			// unreadable key is the same as no key. Reach answers a handset
			// it has no key for in plaintext, so alerts keep arriving —
			// unencrypted, which is worse than intended and far better than
			// a handset that crashes on launch or stops ringing.
			Log.Warning(ex, "Payload key could not be read from secure storage");
			return string.Empty;
		}
	}

	public async Task SavePayloadKeyAsync(string key)
	{
		try
		{
			await SecureStorage.SetAsync(PayloadKeyKey, key).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Payload key could not be written to secure storage");
			throw;
		}
	}

	public Task ClearPayloadKeyAsync()
	{
		try
		{
			SecureStorage.Remove(PayloadKeyKey);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Payload key could not be cleared from secure storage");
		}

		return Task.CompletedTask;
	}

	public async Task<string> GetDeviceTokenAsync()
	{
		try
		{
			return await SecureStorage.GetAsync(DeviceTokenKey).ConfigureAwait(false) ?? string.Empty;
		}
		catch (Exception ex)
		{
			// SecureStorage throws on some Android devices whose keystore
			// has been invalidated (a lock-screen change can do it). An
			// unreadable token is the same as no token: the responder
			// signs in again. Crashing on launch would be worse.
			Log.Warning(ex, "Device token could not be read from secure storage");
			return string.Empty;
		}
	}

	public async Task SaveDeviceTokenAsync(string token)
	{
		try
		{
			await SecureStorage.SetAsync(DeviceTokenKey, token).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			Log.Error(ex, "Device token could not be written to secure storage");
			throw;
		}
	}

	public Task ClearDeviceTokenAsync()
	{
		try
		{
			SecureStorage.Remove(DeviceTokenKey);
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Device token could not be cleared from secure storage");
		}

		return Task.CompletedTask;
	}

	public string DeviceLabel
	{
		get
		{
			var stored = Preferences.Get(DeviceLabelKey, string.Empty);
			return string.IsNullOrWhiteSpace(stored) ? DefaultDeviceLabel() : stored;
		}

		set => Preferences.Set(DeviceLabelKey, value ?? string.Empty);
	}

	/// <summary>
	/// A label describing the hardware, used when the responder has not
	/// set one. Mirrors Register's ResolveDeviceLabel so a fleet shows up
	/// consistently across both apps.
	/// </summary>
	private static string DefaultDeviceLabel()
	{
		try
		{
			var platform = DeviceInfo.Platform;

			if (platform == DevicePlatform.WinUI || platform == DevicePlatform.MacCatalyst)
			{
				var machine = Environment.MachineName;
				if (!string.IsNullOrWhiteSpace(machine)
					&& !string.Equals(machine, "localhost", StringComparison.OrdinalIgnoreCase))
				{
					return machine;
				}
			}

			var manufacturer = (DeviceInfo.Manufacturer ?? string.Empty).Trim();
			var model = (DeviceInfo.Model ?? string.Empty).Trim();

			var hardware = manufacturer.Length > 0
				&& !model.StartsWith(manufacturer, StringComparison.OrdinalIgnoreCase)
				? $"{manufacturer} {model}".Trim()
				: model;

			if (string.IsNullOrWhiteSpace(hardware))
			{
				hardware = "Device";
			}

			var osVersion = (DeviceInfo.VersionString ?? string.Empty).Trim();

			return osVersion.Length == 0
				? $"{hardware} ({platform})"
				: $"{hardware} ({platform} {osVersion})";
		}
		catch
		{
			return "UnknownDevice";
		}
	}

	/// <summary>
	/// Read one setting, preferring the embedded dev credentials when
	/// this is a dev build and falling back to appsettings.json.
	/// </summary>
	private string ReadSetting(string section, string key)
	{
#if USE_DEV_CREDENTIALS
		var fromDev = ReadEmbeddedDevSetting(section, key);
		if (!string.IsNullOrWhiteSpace(fromDev))
		{
			return fromDev;
		}
#endif

		return _configuration.GetSection(section)[key] ?? string.Empty;
	}

	private static string ReadEmbeddedDevSetting(string section, string key)
	{
		try
		{
			var assembly = Assembly.GetExecutingAssembly();
			using var stream = assembly.GetManifestResourceStream(DevCredentialsResource);
			if (stream is null)
			{
				return string.Empty;
			}

			using var document = JsonDocument.Parse(stream);
			if (document.RootElement.TryGetProperty(section, out var sectionElement)
				&& sectionElement.TryGetProperty(key, out var value))
			{
				return value.GetString() ?? string.Empty;
			}
		}
		catch (Exception ex)
		{
			// Malformed dev settings must not stop the app starting; the
			// production path below still applies.
			Log.Warning(ex, "Embedded devsettings.json could not be read for {Section}:{Key}", section, key);
		}

		return string.Empty;
	}
}
