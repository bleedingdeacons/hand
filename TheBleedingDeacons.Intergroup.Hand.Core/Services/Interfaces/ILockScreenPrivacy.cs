namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// What this handset's lock screen does with an alert's text.
///
/// <para><b>Why anyone has to ask.</b> Hand marks its alert notifications
/// private and hands the system a public version reading "Helpline alert
/// / Unlock to read", which is the whole of what Android offers an app.
/// Whether that substitution actually happens is the phone owner's
/// choice, made in Settings and changeable at any moment: where they have
/// chosen to show all notification content — the default on many devices
/// — Android shows the alert's own words to whoever is standing near the
/// phone, and the app cannot override it.</para>
///
/// <para>So the redaction is something Hand <i>offers</i> rather than
/// something it provides, and only the handset can see which it is
/// getting. It reports what it finds so an intergroup can see which
/// handsets are displaying helpline alerts to the room; see Reach's Hand
/// devices screen.</para>
/// </summary>
public interface ILockScreenPrivacy
{
    /// <summary>
    /// The current state, as the wire spells it — one of
    /// <see cref="LockScreenPrivacyState"/>'s constants.
    /// </summary>
    string State { get; }
}

/// <summary>
/// The three answers, spelled as Reach stores them.
///
/// <para>Strings rather than an enum because they cross a wire to a
/// PHP server that keeps them verbatim in a column, and an enum would
/// only be translated back into these at the edge. The spellings are a
/// contract with <c>Reach\Devices\Device::LOCK_SCREEN_*</c>; changing one
/// side alone means a handset whose report is silently discarded.</para>
/// </summary>
public static class LockScreenPrivacyState
{
    /// <summary>
    /// Nobody could tell — a platform with no lock screen worth the name,
    /// or a read that failed.
    ///
    /// <para><b>Not the same as safe.</b> It is the absence of an answer,
    /// and Reach displays it as neither a warning nor a reassurance.
    /// Reported as an empty string, which the server reads as "said
    /// nothing" and leaves whatever it already held — so a handset that
    /// cannot tell can never clear a warning raised when it could.</para>
    /// </summary>
    public const string Unknown = "";

    /// <summary>Sensitive content is hidden; a stranger sees the redacted line.</summary>
    public const string Hidden = "hidden";

    /// <summary>The alert's own words are readable on the lock screen.</summary>
    public const string Shown = "shown";
}
