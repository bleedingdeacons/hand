namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// The fingerprint in front of a handset that is already enrolled.
///
/// <para><b>This is not authentication.</b> Reach decided who this
/// handset belongs to when it was signed in, and the device token is
/// what proves it on every request; nothing here is shown to the server
/// and nothing here can put a handset back on the rota. It answers a
/// smaller and more domestic question — whether the person holding the
/// phone right now is the responder it was handed to — because a duty
/// handset spends its life signed in, unattended, and full of other
/// people's worst days.</para>
///
/// <para><b>It never stands between a responder and an alert.</b> The
/// lock is asked for on a cold start and at no other moment, and it is
/// skipped outright while anything is outstanding. A responder woken at
/// four in the morning presses acknowledge, not a fingerprint sensor.</para>
///
/// <para><b>An unavailable sensor opens the app.</b> Fingerprints are
/// removed, sensors break, and a phone whose owner has just changed
/// their screen lock can lose its enrolments outright. Failing closed
/// would take a certified responder off the rota over a hardware fault
/// no one can fix at midnight, which is a worse outcome than the one the
/// lock exists to prevent — so <see cref="IsAvailableAsync"/> is asked
/// first and a "no" means the app simply opens.</para>
/// </summary>
public interface IAppLock
{
	/// <summary>
	/// Whether this handset can actually ask for a fingerprint right now:
	/// hardware present, and something enrolled on it.
	///
	/// <para>Asynchronous because Windows only answers this
	/// asynchronously, not because either mobile head needs to be.</para>
	/// </summary>
	Task<bool> IsAvailableAsync();

	/// <summary>
	/// Ask for the fingerprint and wait for the answer.
	/// </summary>
	/// <param name="reason">
	/// One line telling the holder what is being unlocked. Some platforms
	/// display it and some ignore it; iOS requires it to be non-empty.
	/// </param>
	Task<AppLockResult> AuthenticateAsync(string reason);
}

/// <summary>
/// How the ask ended.
///
/// <para>Three outcomes rather than a bool, because refusing and being
/// unable to ask lead opposite ways: a refusal keeps the app shut, and
/// an unaskable sensor opens it. Collapsing them would either brick a
/// handset with a broken reader or wave through anyone who taps
/// cancel.</para>
/// </summary>
public enum AppLockResult
{
	/// <summary>The fingerprint matched. Open the app.</summary>
	Unlocked,

	/// <summary>
	/// It did not match, or the prompt was cancelled, or the platform
	/// locked out after too many attempts. Stay shut and offer another go.
	/// </summary>
	Refused,

	/// <summary>
	/// Nothing could be asked — no sensor, nothing enrolled, no activity
	/// to host the prompt, or the platform threw. Open the app; see
	/// <see cref="IAppLock"/> for why that is the safe direction.
	/// </summary>
	Unavailable,
}
