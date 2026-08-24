using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Whether the app can put itself away, and doing it.
///
/// <para>A pass-through, like <see cref="AppLock"/>: each platform's
/// answer is entirely its own and there is nothing shared to hold here.
/// <see cref="IWindowVisibility"/> is the file that explains why the app
/// offers this at all.</para>
/// </summary>
public sealed partial class WindowVisibility : IWindowVisibility
{
	public bool CanHide => PlatformCanHide();

	public void Hide() => PlatformHide();

	private partial bool PlatformCanHide();

	private partial void PlatformHide();
}
