using Serilog.Core;
using Serilog.Events;

namespace TheBleedingDeacons.Intergroup.Hand;

/// <summary>
/// Writes Serilog events to Android's log, so <c>adb logcat</c> shows them
/// as they happen.
///
/// <para><b>Why this exists: there was no live log at all.</b> On a handset
/// the pipeline was a rolling file plus <c>WriteTo.Debug()</c>, and the
/// Debug sink goes to <see cref="System.Diagnostics.Debug"/>, which on
/// Android reaches a listening debugger and nothing else. With the app
/// launched by <c>adb</c> rather than from an IDE — which is every
/// deployment <c>/kick</c> makes — that is no listener, so the only record
/// was the file, readable after the fact with
/// <c>run-as … cat files/logs/…</c> and only by somebody who knew to ask.
/// </para>
///
/// <para>Checked on a real handset on 2026-09-11 before this was written,
/// because the note recording how to read Hand's logs said its output
/// arrives under the <c>app_process64</c> tag: Hand was running, and its
/// process log held not one Serilog line. What arrives under that tag is
/// the runtime's own output, which is a different thing.</para>
///
/// <para><b>Not the Console sink.</b> Serilog's console sink calls
/// <c>Console.set_ForegroundColor</c>, which throws
/// <see cref="PlatformNotSupportedException"/> on Android; every event then
/// lands in SelfLog with a stack trace and drowns what it was meant to
/// show. MauiProgram has carried a comment about that for as long as the
/// app has existed. This writes through <c>Android.Util.Log</c> instead,
/// which is the platform's own API and colours nothing.</para>
///
/// <para><b>Debug builds only</b>, by where it is registered rather than by
/// anything here. The file sink and Better Stack are the record; this is
/// for somebody standing over the handset with a cable.</para>
///
/// <para>Ported from Link, which hit the same wall first. The two are
/// deliberately identical — a diverging copy would be worse than either.
/// </para>
/// </summary>
internal sealed class LogcatSink : ILogEventSink
{
	/// <summary>
	/// The logcat tag, and the thing to filter on:
	/// <c>adb logcat -s Hand:V</c>.
	/// </summary>
	/// <remarks>
	/// A tag of Hand's own, rather than the runtime's <c>app_process64</c>,
	/// which is shared with every managed message on the device. Filtering
	/// that is filtering the platform.
	/// </remarks>
	public const string Tag = "Hand";

	private readonly IFormatProvider? _formatProvider;

	public LogcatSink(IFormatProvider? formatProvider = null) => _formatProvider = formatProvider;

	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		var message = logEvent.RenderMessage(_formatProvider);

		// The exception on its own line rather than folded into the
		// message: logcat wraps on width, and a stack trace appended to a
		// sentence is the shape that gets truncated first.
		if (logEvent.Exception is not null)
		{
			message = message + Environment.NewLine + logEvent.Exception;
		}

		switch (logEvent.Level)
		{
			case LogEventLevel.Verbose:
				global::Android.Util.Log.Verbose(Tag, message);
				break;
			case LogEventLevel.Debug:
				global::Android.Util.Log.Debug(Tag, message);
				break;
			case LogEventLevel.Warning:
				global::Android.Util.Log.Warn(Tag, message);
				break;
			case LogEventLevel.Error:
				global::Android.Util.Log.Error(Tag, message);
				break;
			case LogEventLevel.Fatal:
				// Android has no Fatal that is not an assertion, and Wtf
				// can be configured to kill the process. An app that dies
				// harder because it logged is not a debugging aid — and on
				// this app a Fatal is an alert that did not ring, which is
				// the moment the log matters most.
				global::Android.Util.Log.Error(Tag, message);
				break;
			default:
				global::Android.Util.Log.Info(Tag, message);
				break;
		}
	}
}
