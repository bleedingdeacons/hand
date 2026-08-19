namespace TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

/// <summary>
/// Runs a piece of work on the UI thread.
///
/// <para>A one-method seam over <c>MainThread.InvokeOnMainThreadAsync</c>,
/// which lives in the MAUI workload and so cannot be called from this
/// assembly. That is the immediate reason it exists, but it earns its
/// keep twice over: the collection the alerts page binds to is mutated
/// from a background poll loop and from a push callback, and both have
/// to marshal. Naming that requirement in the constructor is better than
/// leaving it as a static call five levels down.</para>
///
/// <para>Implementations must run <paramref name="action"/> to completion
/// before the returned task completes, and must be safe to call from the
/// UI thread itself — the alert loop does, by way of
/// <c>RefreshAsync</c>.</para>
/// </summary>
public interface IUiDispatcher
{
	/// <summary>Run <paramref name="action"/> on the UI thread.</summary>
	Task InvokeAsync(Action action);
}
