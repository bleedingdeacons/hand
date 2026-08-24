using Serilog;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Android half of <see cref="WindowVisibility"/>.
///
/// <para><c>MoveTaskToBack</c> is what the Home button does, and that is
/// the point: the process stays alive, the poll keeps its schedule, and
/// a full-screen intent can still bring the whole thing back over the
/// lock screen. <c>Finish()</c> would look identical for about a second
/// and then cost the responder their alerts.</para>
///
/// <para>The argument is <c>true</c> — <i>nonRoot</i> — so the whole task
/// goes back rather than only this activity. Hand is a single-activity
/// app, so in practice they are the same thing; passing false would mean
/// the call quietly did nothing if the activity were ever not the root,
/// and a Close button that sometimes does nothing is worse than none.</para>
/// </summary>
public sealed partial class WindowVisibility
{
	/// <summary>Always, on this head. Every Android app can do this.</summary>
	private const bool CanMoveTaskToBack = true;

	private partial bool PlatformCanHide() => CanMoveTaskToBack;

	private partial void PlatformHide()
	{
		// Posted rather than called: the activity is a view-layer object and
		// nothing guarantees the command that got here is still on the main
		// thread by the time it runs.
		MainThread.BeginInvokeOnMainThread(() =>
		{
			try
			{
				var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

				if (activity is null)
				{
					Log.Warning("No activity to send to the back");
					return;
				}

				activity.MoveTaskToBack(true);
			}
			catch (Exception ex)
			{
				// Nothing to recover: the app stays on screen, which is the
				// state it was already in.
				Log.Warning(ex, "Hand could not put itself into the background");
			}
		});
	}
}
