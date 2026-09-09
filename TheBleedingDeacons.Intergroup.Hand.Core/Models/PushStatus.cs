namespace TheBleedingDeacons.Intergroup.Hand.Models;

/// <summary>
/// Whether this handset can actually be pushed to, as one of four
/// answers rather than a bool.
///
/// <para>Two separate things have to be true before Reach can ring a
/// closed handset, and they fail for completely different reasons: the
/// platform has to carry push at all, and the phone's owner has to have
/// left notifications switched on for Hand. A single "push: yes/no"
/// collapses those into one word and tells a responder nothing about
/// which of them to go and fix.</para>
/// </summary>
public enum PushState
{
	/// <summary>
	/// This platform has no push transport. Windows and, for now, the
	/// Apple heads — see <c>PushRegistrar.apple.cs</c>. A poll-only
	/// handset, which is degraded rather than broken.
	/// </summary>
	Unsupported = 0,

	/// <summary>
	/// The transport is there and the phone's owner has turned
	/// notifications off for Hand. The push still arrives; nothing is
	/// shown when it does.
	/// </summary>
	Blocked,

	/// <summary>
	/// Transport and permission both present, and Reach has no
	/// registration for this handset — so it is not being pushed to. The
	/// silent failure this whole indicator exists for.
	/// </summary>
	Unregistered,

	/// <summary>Available and enabled. Push is working.</summary>
	Active,
}

/// <summary>
/// The push indicator's whole content: what to say, and what colour to
/// say it in.
///
/// <para><b>Why a model in Core rather than three properties on the view
/// model.</b> The interesting part is the state machine — which of the
/// three inputs decides the answer, and in what order — and that is
/// exactly the part a view model cannot be unit tested for, because this
/// project's tests cannot reference the MAUI app. Here it is covered by
/// the same test run as everything else.</para>
///
/// <para>The colour is a hex string for the same reason
/// <see cref="HandAlert.LevelBackground"/> is: Core has no MAUI workload
/// and so cannot hold a <c>Color</c> at all. XAML converts it on
/// binding.</para>
/// </summary>
/// <param name="Supported">
/// Whether this platform has a push transport — <c>PushRegistrar.Provider</c>
/// naming one.
/// </param>
/// <param name="Permitted">
/// Whether the phone's own settings still let Hand show a notification.
/// </param>
/// <param name="Registered">
/// Whether Reach holds a push registration for this handset. Read from
/// the session Reach itself returns, so it is the server's answer rather
/// than this side's hope.
/// </param>
public sealed record PushStatus(bool Supported, bool Permitted, bool Registered)
{
	/// <summary>
	/// Before anything has been read. Shows as "not available", which is
	/// the safe way round: a handset briefly under-promising is one
	/// somebody checks, and the opposite is one that goes quiet.
	/// </summary>
	public static PushStatus Unknown { get; } = new(false, false, false);

	/// <summary>
	/// The three inputs collapsed, in the order they can fail. Transport
	/// first, because permission on a platform with no push is not a
	/// problem anybody can act on; permission before registration,
	/// because a registration on a silenced handset would report success
	/// for a phone that shows nothing.
	/// </summary>
	public PushState State =>
		!Supported ? PushState.Unsupported
		: !Permitted ? PushState.Blocked
		: !Registered ? PushState.Unregistered
		: PushState.Active;

	/// <summary>Available and enabled, both.</summary>
	public bool IsActive => State == PushState.Active;

	/// <summary>
	/// True for the two states a responder can do something about, and
	/// false for a platform that simply has no push. Lets the screen give
	/// the fixable cases the room they need without shouting about one
	/// nobody can change.
	/// </summary>
	public bool NeedsAttention => State is PushState.Blocked or PushState.Unregistered;

	public string Headline => State switch
	{
		PushState.Active => "Push notifications are on",
		PushState.Unregistered => "Push notifications are not connected",
		PushState.Blocked => "Push notifications are turned off",
		_ => "Push notifications are not available",
	};

	/// <summary>
	/// What it means for the person holding the handset, in the terms
	/// they care about: whether it will ring, and how late an alert can
	/// be. Never in terms of FCM, tokens or transports.
	/// </summary>
	public string Detail => State switch
	{
		PushState.Active =>
			"Reach can ring this handset the moment an alert is raised, with Hand closed and the screen off.",
		PushState.Unregistered =>
			"This handset can take pushed alerts, but Reach has no registration for it — so nothing is being "
				+ "pushed to it. Alerts still arrive on the poll, up to one interval late.",
		PushState.Blocked =>
			"Notifications are switched off for Hand in this phone's own settings. A pushed alert still arrives, "
				+ "but nothing appears on the screen when it does. Turn them back on there.",
		_ =>
			"This platform has no push, so this handset collects its own alerts by polling. It still rings — an "
				+ "alert can just be up to one poll interval old.",
	};

	/// <summary>
	/// The dot. Green working, amber degraded but still covered by the
	/// poll, red silenced, grey never had it.
	///
	/// <para>Amber rather than red for an unregistered handset because
	/// the poll is carrying it and the responder will still be alerted.
	/// Red is kept for the state where an alert can arrive and show
	/// nothing at all.</para>
	/// </summary>
	public string IndicatorColour => State switch
	{
		PushState.Active => "#2E7D32",
		PushState.Unregistered => "#F9A825",
		PushState.Blocked => "#B3261E",
		_ => "#757575",
	};
}
