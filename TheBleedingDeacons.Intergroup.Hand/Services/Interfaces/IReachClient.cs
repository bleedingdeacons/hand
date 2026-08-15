using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// The HTTP surface Reach exposes to a handset. One method per endpoint,
/// no policy — deciding what to do about a refusal belongs to the
/// services above this.
/// </summary>
public interface IReachClient
{
	/// <summary>
	/// The URL that begins an SSO sign-in in the system browser, and the
	/// redirect the browser will be sent back to.
	/// </summary>
	(Uri Start, Uri Callback) BuildSignInUrls(string provider);

	/// <summary>Trade a one-time code from the browser flow for a device token.</summary>
	Task<ReachResult<DeviceSession>> ExchangeCodeAsync(
		string code, string label, string platform, string pushProvider, string pushToken, CancellationToken cancellationToken);

	/// <summary>Enrol with an email and password, no browser involved.</summary>
	Task<ReachResult<DeviceSession>> SignInWithPasswordAsync(
		string email, string password, string label, string platform, string pushProvider, string pushToken, CancellationToken cancellationToken);

	/// <summary>Who this handset is, and whether it is still allowed.</summary>
	Task<ReachResult<DeviceSession>> GetSessionAsync(string token, CancellationToken cancellationToken);

	/// <summary>Record a rotated push registration token.</summary>
	Task<ReachResult<bool>> UpdatePushTokenAsync(
		string token, string pushProvider, string pushToken, CancellationToken cancellationToken);

	/// <summary>Revoke this handset's token.</summary>
	Task<ReachResult<bool>> SignOutAsync(string token, CancellationToken cancellationToken);

	/// <summary>Alerts this handset should be ringing about.</summary>
	Task<ReachResult<IReadOnlyList<HandAlert>>> GetPendingAlertsAsync(
		string token, CancellationToken cancellationToken);

	/// <summary>Tell Reach this handset has alarmed for an alert.</summary>
	Task<ReachResult<bool>> AcknowledgeAsync(string token, long alertId, CancellationToken cancellationToken);

	/// <summary>
	/// Fetch the contact details attached to an alert.
	///
	/// <para>A separate request on purpose: these are personal data, so
	/// they never travel in the push or the poll. Reach writes an audit
	/// entry for every call, so this should be made when a responder
	/// actually asks — not speculatively alongside the alert.</para>
	/// </summary>
	Task<ReachResult<string>> GetContactAsync(string token, long alertId, CancellationToken cancellationToken);
}
