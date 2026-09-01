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

	/// <summary>
	/// Record a rotated push registration token, and say what this
	/// handset's lock screen does with alert text.
	///
	/// <para><paramref name="lockScreen"/> rides along here rather than
	/// having a call of its own, because Hand re-registers its token at
	/// every launch anyway as the backstop against a silently rotated
	/// one. That makes the report as fresh as a setting its owner can
	/// change at any moment is ever going to be, for no extra request.
	/// Empty means "cannot tell", which the server reads as "said
	/// nothing" and leaves whatever it already held — so a handset that
	/// cannot tell never clears a warning raised when it could.</para>
	/// </summary>
	Task<ReachResult<bool>> UpdatePushTokenAsync(
		string token,
		string pushProvider,
		string pushToken,
		string lockScreen,
		CancellationToken cancellationToken);

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

	/// <summary>
	/// Tell Reach this handset could not read an alert.
	///
	/// <para>Reach can see that a device row has no key. It cannot see a
	/// handset whose own copy has gone, so the handset has to say — and
	/// until it does, the only symptom is a responder who does not
	/// answer.</para>
	///
	/// <para>Carries nothing but the fact. The remedy is the same whatever
	/// the cause, so there is no reason to send one.</para>
	/// </summary>
	Task<ReachResult<bool>> ReportUnreadableAsync(string token, CancellationToken cancellationToken);

	/// <summary>
	/// A page of the member directory, for the recipient picker.
	///
	/// <para>Names and home groups only. A recipient is chosen by id and
	/// resolved to an address on the server — see <see cref="HandMember"/>
	/// on why the wire carries no addresses at all.</para>
	/// </summary>
	Task<ReachResult<IReadOnlyList<HandMember>>> GetMembersAsync(
		string token, string search, int page, CancellationToken cancellationToken);

	/// <summary>The committee tree, flattened, each row carrying its depth.</summary>
	Task<ReachResult<IReadOnlyList<HandCommittee>>> GetCommitteesAsync(
		string token, CancellationToken cancellationToken);

	/// <summary>
	/// Raise a message to one member or to one committee.
	///
	/// <para>Exactly one of <paramref name="memberId"/> and
	/// <paramref name="committeeSlug"/> is sent. Neither is optional in
	/// the sense of "leave both out and it goes to everybody" — the
	/// server refuses that, deliberately, because any responder can send
	/// and a slip must not put the whole rota's phones on.</para>
	/// </summary>
	Task<ReachResult<bool>> SendAlertAsync(
		string token,
		string subject,
		string body,
		string level,
		string response,
		long memberId,
		string committeeSlug,
		CancellationToken cancellationToken);

	/// <summary>
	/// Reply to an alert in free text.
	///
	/// <para>Works after another responder has acknowledged, which is the
	/// point of it: Reach authorises this on whether the alert could have
	/// been sent to this handset, not on who answered it. Hand offers it
	/// from the history for exactly that case.</para>
	///
	/// <para>The text is dispatched onward as an alert, so it reaches a
	/// lock screen. The same rule applies as to everything else: no
	/// personal data.</para>
	/// </summary>
	Task<ReachResult<bool>> ReplyAsync(
		string token, long alertId, string body, CancellationToken cancellationToken);

	/// <summary>
	/// Put a job this handset acknowledged back out to the people it came
	/// from, as a new message.
	///
	/// <para>Refused for anybody who did not acknowledge it, for anything
	/// informational, and for a notice. Carries no parameters: everything
	/// is copied from the alert being passed on.</para>
	/// </summary>
	Task<ReachResult<bool>> ResendAsync(string token, long alertId, CancellationToken cancellationToken);
}
