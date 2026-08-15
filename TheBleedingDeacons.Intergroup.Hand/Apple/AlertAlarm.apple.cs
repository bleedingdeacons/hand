using AVFoundation;
using Foundation;
using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// Apple half of <see cref="AlertAlarm"/>, shared by the iOS and Mac
/// Catalyst heads.
/// </summary>
public sealed partial class AlertAlarm
{
	private AVAudioPlayer? _player;

	/// <summary>
	/// Start the looping alarm.
	///
	/// <para>The audio session category is what decides whether this is
	/// heard at all. <c>Playback</c> sounds even when the ring/silent
	/// switch is set to silent — which is the behaviour a duty handset
	/// needs, and which the default <c>SoloAmbient</c> category does not
	/// give. This is the in-app path only; a closed app cannot set a
	/// session, which is why bypassing silent mode while closed needs
	/// Apple's Critical Alerts entitlement on the push payload
	/// instead.</para>
	///
	/// <para><c>NumberOfLoops = -1</c> loops indefinitely — the app is
	/// running here, so unlike the 30-second cap on a payload sound the
	/// alarm continues until it is acknowledged.</para>
	/// </summary>
	private partial void PlatformStart(HandAlert alert)
	{
		StopPlayer();

		var url = NSBundle.MainBundle.GetUrlForResource("reach_alert", "wav");
		if (url is null)
		{
			Log.Error("reach_alert.wav is not in the app bundle; the in-app alarm will be silent");
			return;
		}

		var session = AVAudioSession.SharedInstance();
		session.SetCategory(AVAudioSessionCategory.Playback);
		session.SetActive(true);

		var player = AVAudioPlayer.FromUrl(url, out var error);
		if (player is null || error is not null)
		{
			Log.Error("Alert audio could not be loaded: {Error}", error?.LocalizedDescription ?? "unknown");
			return;
		}

		player.NumberOfLoops = -1;
		player.Volume = 1.0f;
		player.PrepareToPlay();
		player.Play();

		_player = player;
	}

	private partial void PlatformStop()
	{
		StopPlayer();

		try
		{
			// Hand the audio session back so we are not holding Playback —
			// otherwise the handset stays in a state that can duck other
			// apps long after the alarm has finished.
			AVAudioSession.SharedInstance().SetActive(false);
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Audio session could not be deactivated");
		}
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
			player.Stop();
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "AVAudioPlayer could not be stopped");
		}
		finally
		{
			player.Dispose();
		}
	}
}
