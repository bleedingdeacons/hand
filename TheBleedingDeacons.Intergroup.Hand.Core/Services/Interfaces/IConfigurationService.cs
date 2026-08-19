using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Everything the app needs to know that is not in the code: where Reach
/// is, where logs go, and the device token proving who this handset is.
/// </summary>
public interface IConfigurationService
{
	/// <summary>
	/// Better Stack log-shipping settings. Synchronous because the
	/// logging pipeline is rebuilt during startup, before there is an
	/// await to hang off.
	/// </summary>
	BetterStackConfiguration GetBetterStackConfiguration();

	ReachConfiguration GetReachConfiguration();

	Task SaveReachConfigurationAsync(ReachConfiguration configuration);

	/// <summary>
	/// The bearer token this handset authenticates with, or empty when it
	/// has not been enrolled. Held in platform secure storage — the
	/// keystore on Android, the keychain on Apple platforms, DPAPI on
	/// Windows — because it is a long-lived credential for a system that
	/// dispatches helpline work.
	/// </summary>
	Task<string> GetDeviceTokenAsync();

	Task SaveDeviceTokenAsync(string token);

	Task ClearDeviceTokenAsync();

	/// <summary>
	/// A human label for this handset, shown in Reach's admin list so an
	/// administrator can tell one enrolled device from another.
	/// </summary>
	string DeviceLabel { get; set; }
}
