using Serilog;
using TheBleedingDeacons.Intergroup.Hand.Models;
using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// The in-app alarm: a looping sound plus vibration, running until a
/// responder acknowledges.
///
/// <para>Looping rather than a one-shot chime is the whole point. A duty
/// handset is face-down on a table in another room; a single notification
/// tone is missed, and a missed helpline alert is somebody not being
/// called back. It behaves like a ringing phone because that is what it
/// is standing in for.</para>
///
/// <para>Audio and vibration are per-platform (see the partial methods
/// below); the state machine that decides when they run is here, so all
/// four heads agree on the behaviour and only the mechanics differ.</para>
/// </summary>
public sealed partial class AlertAlarm : IAlertAlarm
{
	/// <summary>
	/// Vibration pattern, repeated while the alarm sounds: a long buzz,
	/// a short gap. Deliberately unlike any messaging app's pattern, so a
	/// responder can tell a helpline alert from a text without looking.
	/// </summary>
	private static readonly TimeSpan VibrateOn = TimeSpan.FromMilliseconds(800);
	private static readonly TimeSpan VibrateGap = TimeSpan.FromMilliseconds(400);

	private readonly SemaphoreSlim _gate = new(1, 1);

	private CancellationTokenSource? _vibrationLoop;

	public bool IsSounding { get; private set; }

	public async Task StartAsync(HandAlert alert)
	{
		ArgumentNullException.ThrowIfNull(alert);

		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			// Already sounding: a second alert does not start a second
			// alarm. The list on screen shows both; the noise is one noise.
			if (IsSounding)
			{
				return;
			}

			IsSounding = true;

			try
			{
				PlatformStart(alert);
			}
			catch (Exception ex)
			{
				// A handset that cannot play audio — a broken codec, an
				// audio focus refusal, a missing resource — must still
				// vibrate and still show the alert. Losing the sound is bad;
				// losing the alert because the sound failed would be worse.
				Log.Error(ex, "Alert audio could not be started for alert {AlertId}", alert.Id);
			}

			StartVibrating();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task StopAsync()
	{
		await _gate.WaitAsync().ConfigureAwait(false);
		try
		{
			if (!IsSounding)
			{
				return;
			}

			IsSounding = false;

			StopVibrating();

			try
			{
				PlatformStop();
			}
			catch (Exception ex)
			{
				Log.Error(ex, "Alert audio could not be stopped cleanly");
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private void StartVibrating()
	{
		var cts = new CancellationTokenSource();
		_vibrationLoop = cts;

		// Fire-and-forget by design: the loop's whole job is to keep going
		// until cancelled, and awaiting it would block the alert path.
		_ = Task.Run(
			async () =>
			{
				try
				{
					while (!cts.IsCancellationRequested)
					{
						Vibration.Default.Vibrate(VibrateOn);
						await Task.Delay(VibrateOn + VibrateGap, cts.Token).ConfigureAwait(false);
					}
				}
				catch (OperationCanceledException)
				{
					// Normal stop.
				}
				catch (FeatureNotSupportedException)
				{
					// Desktop. The sound is the alarm there.
				}
				catch (Exception ex)
				{
					Log.Debug(ex, "Vibration loop ended unexpectedly");
				}
			},
			cts.Token);
	}

	private void StopVibrating()
	{
		var cts = _vibrationLoop;
		_vibrationLoop = null;

		if (cts is null)
		{
			return;
		}

		cts.Cancel();
		cts.Dispose();

		try
		{
			Vibration.Default.Cancel();
		}
		catch (Exception ex)
		{
			Log.Debug(ex, "Vibration could not be cancelled");
		}
	}

	// Implemented per platform under Platforms/. Declared partial rather
	// than abstract so each head compiles only its own audio stack.
	private partial void PlatformStart(HandAlert alert);

	private partial void PlatformStop();
}
