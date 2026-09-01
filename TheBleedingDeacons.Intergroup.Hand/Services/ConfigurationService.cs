using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Serilog;
#if IOS
using TheBleedingDeacons.Intergroup.Hand.NotificationService;
#endif
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

	/// <summary>
	/// Secure-storage key for this handset's device token.
	///
	/// <para>Internal for the same reason as <see cref="PayloadKeyKey"/>:
	/// a push can arrive with no app behind it, and
	/// <see cref="Platforms.Android.HeadlessAlerts"/> then has to
	/// authenticate a fault report with no container to build this
	/// service from.</para>
	/// </summary>
	internal const string DeviceTokenKey = "hand_device_token";
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
	private const string AppLockKey = "app_lock_enabled";
	private const string ReachBaseUrlKey = "reach_base_url";

	/// <summary>
	/// Where the last resolved server address is mirrored, for a reader
	/// with no container.
	///
	/// <para>Not the same entry as <see cref="ReachBaseUrlKey"/> and
	/// deliberately so. That one holds what a responder <i>chose</i>, and
	/// is empty on a handset happily using the address built into the
	/// package — which is most of them. This one holds whichever address
	/// was actually used, wherever it came from, so
	/// <see cref="Platforms.Android.HeadlessAlerts"/> can reach the server
	/// without an <c>IConfiguration</c> to read the embedded settings
	/// from.</para>
	///
	/// <para>Written only, never read back by
	/// <see cref="GetReachConfiguration"/>. Folding it into that lookup
	/// would make a stale mirror outrank a new build's built-in default,
	/// so a rebuild pointed at a different server would be quietly
	/// ignored.</para>
	/// </summary>
	internal const string ReachResolvedBaseUrlKey = "reach_base_url_resolved";
	private const string ReachPollSecondsKey = "reach_poll_seconds";
	// Deliberately a NEW key rather than a rename of reach_on_duty. The
	// two mean opposite things — the old one defaulted to true meaning
	// "alerting", this defaults to false meaning "audible" — so reusing
	// the key would read every existing handset's "on duty" as "in a
	// meeting" and silence the whole rota on upgrade. A fresh key means
	// every handset comes back loud, which is the safe direction.
	private const string ReachInMeetingKey = "reach_in_meeting";
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

		var configuration = new ReachConfiguration
		{
			BaseUrl = baseUrl,
			PollSeconds = pollSeconds > 0 ? pollSeconds : 20,
			InMeeting = Preferences.Get(ReachInMeetingKey, false),
			Poll = Preferences.Get(ReachPollEnabledKey, true),
		}.Normalised();

		// Mirrored for the headless reader. See ReachResolvedBaseUrlKey.
		// Compared first because this runs on every REST call the app
		// makes, and rewriting an unchanged value would put a preferences
		// write behind each one.
		if (!string.Equals(
			Preferences.Get(ReachResolvedBaseUrlKey, string.Empty),
			configuration.BaseUrl,
			StringComparison.Ordinal))
		{
			Preferences.Set(ReachResolvedBaseUrlKey, configuration.BaseUrl);
		}

		return configuration;
	}

	public Task SaveReachConfigurationAsync(ReachConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var normalised = configuration.Normalised();
		Preferences.Set(ReachBaseUrlKey, normalised.BaseUrl);
		Preferences.Set(ReachPollSecondsKey, normalised.PollSeconds);
		Preferences.Set(ReachInMeetingKey, normalised.InMeeting);
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

#if IOS
		// And again, into the shared keychain group, because the
		// notification extension cannot read the entry above.
		//
		// Written to both rather than moved: the app itself keeps using
		// SecureStorage, which is the right thing everywhere the app is the
		// only reader, and a handset whose shared-group entitlement is not
		// yet provisioned still has a working app. The extension is the only
		// thing that needs the second copy, and it is also the only thing
		// that fails without it.
		if (!SharedKeychain.Write(key))
		{
			// Not fatal. It means the notification extension will not be able
			// to open alerts on this handset, which shows as "could not read"
			// on the lock screen rather than as silence.
			Log.Warning("Payload key could not be written to the shared keychain; the notification extension will not be able to decrypt");
		}
#endif
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

#if IOS
		// Signing out has to forget both copies, or the extension would go
		// on decrypting alerts for a handset that is no longer enrolled.
		try
		{
			SharedKeychain.Delete();
		}
		catch (Exception ex)
		{
			Log.Warning(ex, "Payload key could not be cleared from the shared keychain");
		}
#endif

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
	/// Whether opening this handset asks for a fingerprint first.
	///
	/// <para>Preferences, not secure storage, and <b>on</b> by default. See
	/// <see cref="IConfigurationService.AppLockEnabled"/> for both
	/// reasons, and for why defaulting it on is safe on a handset that has
	/// no fingerprint to give.</para>
	/// </summary>
	public bool AppLockEnabled
	{
		get => Preferences.Get(AppLockKey, true);

		set => Preferences.Set(AppLockKey, value);
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
