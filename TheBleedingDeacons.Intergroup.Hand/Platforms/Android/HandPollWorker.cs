using Android.Content;
using AndroidX.Work;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;
using JavaClass = Java.Lang.Class;
using TimeUnit = Java.Util.Concurrent.TimeUnit;

namespace TheBleedingDeacons.Intergroup.Hand.Platforms.Android;

/// <summary>
/// The poll that runs when the app does not.
///
/// <para>Hand's own poll is a timer inside <c>AlertService</c>, so it
/// exists only while the app is running — which on a duty handset is the
/// exception rather than the rule. Push covers the rest, and push is the
/// fast path but never the certain one: a registration token can rotate
/// silently, Play Services can be missing, and a handset that enrolled
/// poll-only has no push at all. This is the floor underneath all of
/// that. It is not meant to be prompt; it is meant to mean that a
/// handset which was going to stay silent all night does not.</para>
///
/// <para><b>Fifteen minutes because that is the floor WorkManager
/// allows</b>, not because fifteen is a good answer for a helpline. An
/// alert arriving by this route is late by any standard that matters —
/// the point is that it arrives at all, on a handset where the two
/// quicker routes have both failed.</para>
///
/// <para><b>There is deliberately no way to cancel this.</b> Signing out
/// clears the device token, and the work stops at the token check a few
/// lines into <see cref="DoWork"/> — so a signed-out handset wakes every
/// fifteen minutes, reads one value and goes straight back to sleep.
/// That costs less than the code to tear the schedule down and stand it
/// back up would, and it means a responder signing back in is already
/// being polled for.</para>
///
/// <para>WorkManager rather than a boot receiver and a repeating alarm.
/// Periodic work survives a reboot on its own — WorkManager registers
/// its own BOOT_COMPLETED receiver and rebuilds the queue — and survives
/// an app update too, which is the case a hand-rolled alarm quietly
/// loses. It also defers to Doze rather than fighting it, so this costs
/// a duty phone's battery very little.</para>
/// </summary>
public sealed class HandPollWorker : Worker
{
	/// <summary>
	/// Names the single periodic job, so enqueuing it again replaces or
	/// keeps the existing one rather than stacking a second.
	/// </summary>
	private const string WorkName = "hand-alert-poll";

	/// <summary>WorkManager's minimum period. Anything lower is silently raised to it.</summary>
	private static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

	public HandPollWorker(Context context, WorkerParameters workerParameters)
		: base(context, workerParameters)
	{
	}

	/// <summary>
	/// Put the periodic poll in place, if it is not already.
	/// </summary>
	/// <remarks>
	/// <para>Called at every launch rather than only at sign-in, and
	/// <see cref="ExistingPeriodicWorkPolicy.Keep"/> is what makes that
	/// safe: an existing schedule is left alone, so relaunching does not
	/// reset the interval and starve the poll by restarting its clock.
	/// Scheduling before sign-in costs nothing, because the work itself
	/// stops at the token check.</para>
	/// </remarks>
	public static void Schedule(Context context)
	{
		try
		{
			// Nothing here can work without a network, and letting
			// WorkManager hold the job until there is one is far cheaper
			// than waking to discover there isn't.
			var constraints = new Constraints.Builder()
				.SetRequiredNetworkType(NetworkType.Connected)!
				.Build();

			var request = new PeriodicWorkRequest.Builder(
					JavaClass.FromType(typeof(HandPollWorker)),
					(long)Period.TotalMinutes,
					TimeUnit.Minutes!)
				.SetConstraints(constraints!)!
				.Build();

			WorkManager.GetInstance(context)!.EnqueueUniquePeriodicWork(
				WorkName,
				ExistingPeriodicWorkPolicy.Keep,
				request!);

			Log.Information(
				"Background alert poll scheduled every {Minutes} minutes",
				Period.TotalMinutes);
		}
		catch (Exception ex)
		{
			// A handset without the background poll is the handset this app
			// shipped with until now: push still works, and the in-app poll
			// still runs while it is open. Not worth failing a launch over.
			Log.Warning(ex, "The background alert poll could not be scheduled");
		}
	}

	/// <summary>
	/// Ask Reach what is outstanding, and put any of it on screen.
	/// </summary>
	/// <remarks>
	/// Runs on a WorkManager background thread with a ten-minute budget,
	/// so blocking on the async calls is both safe and the simplest
	/// correct thing. Nothing here touches the UI.
	/// </remarks>
	public override Result DoWork()
	{
		try
		{
			return PollAsync().GetAwaiter().GetResult();
		}
		catch (Exception ex)
		{
			// Never let a background poll crash the process. Retry rather
			// than Failure: whatever went wrong, the next window is a
			// perfectly good time to try again.
			Log.Error(ex, "The background alert poll failed");
			return Result.InvokeRetry()!;
		}
	}

	private static async Task<Result> PollAsync()
	{
		var configuration = Resolve<IConfigurationService>();
		var reach = Resolve<IReachClient>();

		if (configuration is null || reach is null)
		{
			// The container builds with the process, so this means the
			// process is only half up. Nothing is lost by waiting.
			Log.Warning("The background alert poll ran before the app was ready");
			return Result.InvokeRetry()!;
		}

		var settings = configuration.GetReachConfiguration();

		// <b>No duty gate any more.</b> This used to stop the background
		// poll when the responder was off duty, which meant a handset that
		// had quietly left the rota. Meeting mode replaced it and changes
		// only the volume — see ReachConfiguration.InMeeting — so the poll
		// always runs and the noise is decided per alert.

		// Polling turned off in Settings. The schedule is left in place
		// rather than cancelled, so turning it back on takes effect at the
		// next window instead of needing the app reopened to rebuild it.
		if (!settings.Poll)
		{
			return Result.InvokeSuccess()!;
		}

		var token = await configuration.GetDeviceTokenAsync().ConfigureAwait(false);
		if (string.IsNullOrEmpty(token))
		{
			// Not enrolled. Also the ordinary state between installing the
			// app and signing in, which is why Schedule does not wait for
			// sign-in — this check is the wait.
			return Result.InvokeSuccess()!;
		}

		var result = await reach.GetPendingAlertsAsync(token, CancellationToken.None).ConfigureAwait(false);
		if (!result.Success)
		{
			// A 401 is not handled here. Signing a handset out is a
			// decision with a screen attached to it — AlertService raises
			// AuthenticationLost and the app explains itself — and doing it
			// silently from a background poll would take a phone off the
			// rota with nobody told. The next launch finds the same 401.
			Log.Debug(
				"The background alert poll could not reach Reach: {Failure} {Message}",
				result.Failure, result.Message);

			return result.Failure == ReachFailure.Network
				? Result.InvokeRetry()!
				: Result.InvokeSuccess()!;
		}

		var shown = 0;
		foreach (var alert in result.Value ?? [])
		{
			if (await HeadlessAlerts.TryPresentAsync(alert).ConfigureAwait(false))
			{
				shown++;
			}
		}

		if (shown > 0)
		{
			Log.Information("The background alert poll raised {Count} alert(s)", shown);
		}

		return Result.InvokeSuccess()!;
	}

	private static T? Resolve<T>()
		where T : class
	{
		return IPlatformApplication.Current?.Services.GetService<T>();
	}
}
