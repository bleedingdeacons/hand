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
	/// The key Reach encrypts alert payloads to, or empty when this
	/// handset has none — enrolled before keys existed, or on a device
	/// whose keystore has since been invalidated.
	///
	/// <para>Empty is not an error. Reach sends plaintext to a handset it
	/// has no key for, so an alert still arrives and still rings.</para>
	/// </summary>
	Task<string> GetPayloadKeyAsync();

	Task SavePayloadKeyAsync(string key);

	Task ClearPayloadKeyAsync();

	/// <summary>
	/// A human label for this handset, shown in Reach's admin list so an
	/// administrator can tell one enrolled device from another.
	/// </summary>
	string DeviceLabel { get; set; }

	/// <summary>
	/// Whether this handset asks for a fingerprint when it is opened.
	///
	/// <para><b>On unless a responder turns it off.</b> A duty handset
	/// holds other people's worst days and spends its life signed in and
	/// unattended, so the protective setting is the one that should not
	/// need finding — and the cost of defaulting it on is nil where it
	/// cannot be honoured: a handset with nothing enrolled is asked
	/// nothing and opens as it always did. See
	/// <see cref="IAppLock.IsAvailableAsync"/>.</para>
	///
	/// <para>Kept in preferences rather than secure storage: it is a
	/// stated preference, not a secret, and nothing is protected by hiding
	/// it. A handset whose preferences can be read is one where the lock
	/// has already been got round by other means.</para>
	///
	/// <para>Deliberately left on when the sensor stops working. See
	/// <see cref="IAppLock"/>: the answer to a fingerprint that cannot be
	/// asked for is to open the app, not to quietly forget a setting the
	/// responder chose and would never be told had lapsed.</para>
	/// </summary>
	bool AppLockEnabled { get; set; }
}
