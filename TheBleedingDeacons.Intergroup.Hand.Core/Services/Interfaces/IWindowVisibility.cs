namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Putting Hand out of sight without putting it out of action.
///
/// <para><b>Closing is not stopping, and the two must not look alike.</b>
/// A responder who wants the helpline off their screen has one obvious
/// control to reach for, and on a duty handset the obvious one is the
/// wrong one: sign out and the phone goes quiet for the whole shift with
/// nothing to say so. This is the right one — the app goes away, the
/// poll keeps running, the push keeps arriving, and the handset still
/// rings.</para>
///
/// <para><b>Not every platform will do it.</b> Android moves its own task
/// to the back and Windows minimises; the Apple heads refuse outright,
/// because Apple gives an app no supported way to put itself into the
/// background and treats one that appears to quit itself as a crash from
/// the holder's point of view. Where <see cref="CanHide"/> is false the
/// button is not offered at all, rather than offered and inert.</para>
/// </summary>
public interface IWindowVisibility
{
	/// <summary>
	/// Whether this platform lets the app put itself away. False means the
	/// only way out of Hand is the one the operating system provides.
	/// </summary>
	bool CanHide { get; }

	/// <summary>
	/// Put the app out of sight. Does nothing where <see cref="CanHide"/>
	/// is false, and stops the alert loop nowhere.
	/// </summary>
	void Hide();
}
