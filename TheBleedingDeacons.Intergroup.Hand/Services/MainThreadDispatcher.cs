using TheBleedingDeacons.Intergroup.Hand.Services.Interfaces;

namespace TheBleedingDeacons.Intergroup.Hand.Services;

/// <summary>
/// The real <see cref="IUiDispatcher"/>: MAUI's main thread.
///
/// <para>The whole implementation is the one call it forwards. It lives
/// in the app rather than in Hand.Core because <c>MainThread</c> comes
/// from the MAUI workload, which is precisely why the interface exists —
/// see <see cref="IUiDispatcher"/>.</para>
/// </summary>
public sealed class MainThreadDispatcher : IUiDispatcher
{
	public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);
}
