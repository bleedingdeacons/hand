using Microsoft.UI.Windowing;
using Serilog;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Windows half of <see cref="WindowVisibility"/>.
///
/// <para><b>Minimised, not hidden.</b> Hiding the window outright is one
/// call away and would leave a responder with a running app and no way
/// back to it: this head has no tray icon yet, so an invisible window is
/// an app that has to be killed in Task Manager. Minimising puts it on
/// the taskbar, where it can be clicked — which is what a responder
/// means by closing it anyway.</para>
///
/// <para>The alarm and the poll are unaffected either way. See
/// <see cref="AlertAlarm"/> for why this head has to stay resident at
/// all.</para>
/// </summary>
public sealed partial class WindowVisibility
{
	/// <summary>
	/// Every overlapped window has a minimise; see PlatformHide for the
	/// presenters that do not.
	/// </summary>
	private const bool CanMinimise = true;

	private partial bool PlatformCanHide() => CanMinimise;

	private partial void PlatformHide()
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			try
			{
				var window = Application.Current?.Windows.FirstOrDefault();

				if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window native)
				{
					Log.Warning("No native window to minimise");
					return;
				}

				// Only an overlapped window has a minimise; a full-screen or
				// compact presenter has nowhere to go, and asking anyway
				// would throw on the UI thread.
				if (native.AppWindow?.Presenter is OverlappedPresenter presenter)
				{
					presenter.Minimize();
				}
			}
			catch (Exception ex)
			{
				Log.Warning(ex, "Hand could not minimise its window");
			}
		});
	}
}
