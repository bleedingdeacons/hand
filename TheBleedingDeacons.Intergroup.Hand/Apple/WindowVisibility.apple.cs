namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Apple half of <see cref="WindowVisibility"/>. Says no, and means it.
///
/// <para>iOS gives an app no supported way to put itself into the
/// background. <c>UIApplication</c> offers nothing for it, the private
/// selector that does is a rejection at review, and Apple's own guidance
/// is explicit that an app which appears to quit itself reads as a crash
/// to the person holding the phone. The Home gesture and the App
/// Switcher are the answer, and they are the responder's to use.</para>
///
/// <para>Mac Catalyst is the same answer for a duller reason: the window
/// could be ordered out through AppKit, but Catalyst does not surface
/// AppKit, and that head already has a window manager the responder
/// knows how to drive.</para>
///
/// <para>So the button is not offered here at all. A control that does
/// nothing would teach a responder that Close is unreliable, on the one
/// screen where they most need to believe what it says.</para>
/// </summary>
public sealed partial class WindowVisibility
{
	/// <summary>Never, on either Apple head. See the class docblock.</summary>
	private const bool CanBackgroundItself = false;

	private partial bool PlatformCanHide() => CanBackgroundItself;

	private partial void PlatformHide()
	{
		// Deliberately empty. CanHide is false, so nothing offers this.
	}
}
