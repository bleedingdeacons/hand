using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Windows half of <see cref="AlertAlarm"/>.
///
/// <para>The desktop head has no push at all, so this alarm is the whole
/// audible story on Windows — there is no system-played sound behind it
/// the way there is on the mobile heads. That is why Hand runs resident
/// in the tray on this platform: a closed process cannot be woken, so
/// "closed" has to mean "not on screen".</para>
/// </summary>
public sealed partial class AlertAlarm
{
	private MediaPlayer? _player;

	private partial void PlatformStart(HandAlert alert)
	{
		StopPlayer();

		var player = new MediaPlayer
		{
			// Loops until acknowledged, like the mobile heads.
			IsLoopingEnabled = true,
			Volume = 1.0,

			// ms-appx:// resolves against the packaged app's content. The
			// wav is a MauiAsset, so it lands under Resources/Raw.
			Source = MediaSource.CreateFromUri(new Uri("ms-appx:///Resources/Raw/reach_alert.wav")),
		};

		player.Play();
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
			player.Pause();
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "MediaPlayer could not be paused");
		}
		finally
		{
			player.Dispose();
		}
	}
}
