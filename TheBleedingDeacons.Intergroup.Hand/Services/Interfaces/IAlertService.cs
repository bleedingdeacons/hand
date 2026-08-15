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
	/// Raised when Reach stops recognising this handset — revoked, or the
	/// responder is no longer certified. The app drops its token and shows
	/// sign-in. This is how a withdrawn certification reaches a responder
	/// as a message rather than as alerts that quietly stop.
	/// </summary>
	event EventHandler? AuthenticationLost;

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
}
