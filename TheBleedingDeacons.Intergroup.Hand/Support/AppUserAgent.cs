using TheBleedingDeacons.Intergroup.Hand.Services;

namespace TheBleedingDeacons.Intergroup.Hand.Support;

/// <summary>
/// Reads this handset's four facts and hands them to
/// <see cref="UserAgent"/>.
/// </summary>
/// <remarks>
/// <para>Split from the builder because the builder lives in Hand.Core,
/// which has no MAUI workload and therefore cannot see
/// <c>AppInfo</c>, <c>DeviceInfo</c> or <c>Preferences</c>. The string
/// building is the part worth testing and it went where a test project can
/// reach it; this is the part that can only run on a device.</para>
///
/// <para>The address comes from the mirror <c>ConfigurationService</c>
/// keeps rather than the configuration object, for the same reason
/// <c>HeadlessAlerts</c> reads it: this runs on paths with no container to
/// ask. It is written on every REST call the app makes, so by the time
/// anything is being sent it holds wherever the handset is actually
/// pointed. Before that — a log upload on a first launch, say — it is
/// empty, and the header says "unknown" rather than guessing.</para>
/// </remarks>
internal static class AppUserAgent
{
	/// <summary>
	/// The header as it stands right now. A method rather than a property
	/// because it is the callback a <see cref="UserAgentHandler"/> holds,
	/// and because the answer changes when the server address does.
	/// </summary>
	public static string Current() => UserAgent.ForApp(
		UserAgent.Product,
		AppInfo.Current.VersionString,
		DeviceInfo.Current.Platform.ToString(),
		Preferences.Get(ConfigurationService.ReachResolvedBaseUrlKey, string.Empty));
}
