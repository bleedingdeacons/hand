using Android.Content;
using Android.Media;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Stream = Android.Media.Stream;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Android half of <see cref="AlertAlarm"/>.
/// </summary>
public sealed partial class AlertAlarm
{
	private MediaPlayer? _player;

	/// <summary>
	/// Start the looping alarm.
	///
	/// <para>The audio attributes are the important part. <c>Usage =
	/// Alarm</c> routes the sound to the alarm stream, which is not
	/// silenced by the ringer being down and is not affected by
	/// Do Not Disturb's default rules — which is what a duty handset
	/// needs and what an ordinary notification usage would not give. <c>ContentType = Sonification</c> tells the system this is a
	/// functional sound rather than music, so it is not ducked by other
	/// audio.</para>
	///
	/// <para>Audio focus is requested but not required: a refusal (a call
	/// in progress, say) still leaves the alarm playing at whatever volume
	/// the system allows. An alarm that declines to sound because another
	/// app said no is not an alarm.</para>
	/// </summary>
	private partial void PlatformStart(HandAlert alert)
	{
		StopPlayer();

		var context = Android.App.Application.Context;
		var uri = Android.Net.Uri.Parse(
			$"{ContentResolver.SchemeAndroidResource}://{context.PackageName}/{Resource.Raw.reach_alert}");

		if (uri is null)
		{
			// Only reachable if the resource id stopped resolving, which
			// would be a packaging fault rather than a runtime one — but the
			// alarm must not take the alert down with it, and the vibration
			// still runs.
			Log.Error("The alert sound URI could not be built; the in-app alarm will be silent");
			return;
		}

		var player = new MediaPlayer();

		player.SetAudioAttributes(
			new AudioAttributes.Builder()!
				.SetUsage(AudioUsageKind.Alarm)!
				.SetContentType(AudioContentType.Sonification)!
				.Build()!);

		player.SetDataSource(context, uri);
		player.Looping = true;
		player.Prepare();
		player.Start();

		_player = player;
	}

	private partial void PlatformStop()
	{
		StopPlayer();
	}

	private void StopPlayer()
	{
		var player = _player;
		_player = null;

		if (player is null)
		{
			return;
		}

		try
		{
			if (player.IsPlaying)
			{
				player.Stop();
			}
		}
		catch (Exception ex)
		{
			// MediaPlayer throws IllegalStateException if it has already
			// been torn down under us. Nothing to do but note it.
			Log.Debug(ex, "MediaPlayer could not be stopped");
		}
		finally
		{
			player.Release();
			player.Dispose();
		}
	}
}
