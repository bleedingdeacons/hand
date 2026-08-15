using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Where the platform's push registration token comes from.
///
/// <para>The shared half answers for the platforms that have no push at
/// all. Android and iOS override it under Platforms/.</para>
/// </summary>
public sealed partial class PushRegistrar : IPushRegistrar
{
	public const string Fcm = "fcm";

	public string Provider => PlatformProvider();

	public Task<string> GetTokenAsync() => PlatformGetTokenAsync();

	private partial string PlatformProvider();

	private partial Task<string> PlatformGetTokenAsync();
}
