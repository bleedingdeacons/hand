using System.Collections.ObjectModel;
using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Owns the alert loop: polling, receiving pushes, de-duplicating between
/// the two, raising the alarm, and acknowledging.
/// </summary>
public interface IAlertService
{
	/// <summary>
	/// Live alerts this handset has not yet acknowledged, newest first.
	/// Bound directly by the alerts page.
	/// </summary>
	ObservableCollection<HandAlert> Active { get; }

	/// <summary>
	/// Raised when Reach stops recognising this handset — revoked,
	/// removed, or the responder is no longer certified. The app drops its
	/// token and shows sign-in. This is how a withdrawn certification
	/// reaches a responder as a message rather than as alerts that quietly
	/// stop, which is why the event carries one:
	/// <see cref="AuthenticationLostEventArgs.Reason"/> is put in front of
	/// the responder on the sign-in screen.
	/// </summary>
	event EventHandler<AuthenticationLostEventArgs>? AuthenticationLost;

	/// <summary>Begin polling. Safe to call when already started.</summary>
	Task StartAsync();

	/// <summary>Stop polling and silence any alarm.</summary>
	Task StopAsync();

	/// <summary>Poll once, now — on resume, or on pull-to-refresh.</summary>
	Task RefreshAsync();

	/// <summary>
	/// Handle an alert that arrived by push. Called from the platform's
	/// messaging service, which may be running with no UI.
	/// </summary>
	Task HandlePushAsync(HandAlert alert);

	/// <summary>
	/// Tell Reach this handset cannot read what it is sent.
	///
	/// <para>Called by the platform's messaging service when a push
	/// arrived sealed and would not open. It cannot be triggered from an
	/// alert any more, because an alert that will not open never becomes
	/// one — <see cref="HandAlert.FromPushData"/> returns null and the
	/// push is ignored. Without this the handset would simply go quiet,
	/// which is the exact failure the report exists to make visible: it
	/// is what puts the handset on Reach's devices screen.</para>
	///
	/// <para>At most once per run, and failures are swallowed. It is a
	/// diagnostic, not a delivery.</para>
	/// </summary>
	Task ReportUnreadableAsync();

	/// <summary>
	/// Acknowledge an alert: tell Reach this handset has rung for it,
	/// remove it from <see cref="Active"/>, and stop the alarm if nothing
	/// else is outstanding.
	/// </summary>
	Task AcknowledgeAsync(HandAlert alert);

	/// <summary>Acknowledge everything currently outstanding.</summary>
	Task AcknowledgeAllAsync();

	/// <summary>
	/// Fetch and reveal an alert's contact details, if it has any.
	///
	/// <para>Called when a responder taps for them, never automatically:
	/// Reach audits every read, and an audit trail full of reads nobody
	/// asked for tells a regulator nothing useful.</para>
	/// </summary>
	Task ShowContactAsync(HandAlert alert);

	/// <summary>
	/// Send a free-text reply about an alert, by its id.
	///
	/// <para><b>Takes an id rather than an alert, because the card is
	/// frequently gone.</b> The case this exists for is a responder
	/// replying after somebody else answered — at which point Reach has
	/// stopped serving the message and Hand has removed every local copy,
	/// so the only thing left is the history entry and its id. The server
	/// authorises on whether the alert could have been sent here, not on
	/// who acknowledged it, so the reply lands.</para>
	///
	/// <para>Replying settles nothing. It is not a second person taking
	/// the job on, and an alert still outstanding stays outstanding.</para>
	/// </summary>
	/// <returns>Whether Reach accepted it.</returns>
	Task<bool> ReplyAsync(long alertId, string body);

	/// <summary>
	/// Put a job this handset acknowledged back out to the rota.
	///
	/// <para>For the responder who took a call and then could not do it.
	/// Reach raises it again as a genuinely new message, to the people the
	/// original went to, carrying its contact details — so whoever picks
	/// it up can still ring the caller.</para>
	///
	/// <para>Unlike <see cref="ReplyAsync"/> this <em>does</em> finish the
	/// alert here: the job is no longer this responder's, so the card goes
	/// and the history entry says it was passed on.</para>
	/// </summary>
	/// <returns>Whether Reach accepted it.</returns>
	Task<bool> ResendAsync(HandAlert alert);
}

/// <summary>
/// Why a handset stopped being signed in.
///
/// <para>A reason rather than a code, because its only consumer is the
/// sign-in screen and its only job is to be read by whoever picks the
/// handset up. A responder who finds themselves signed out at the start
/// of a shift needs to know whether to sign back in or to ring the
/// intergroup, and those are different answers.</para>
/// </summary>
public sealed class AuthenticationLostEventArgs(string reason) : EventArgs
{
	/// <summary>A sentence to show the responder. Never empty.</summary>
	public string Reason { get; } =
		string.IsNullOrWhiteSpace(reason)
			? "This handset has been signed out. Sign in again to put it back on the rota."
			: reason;
}
