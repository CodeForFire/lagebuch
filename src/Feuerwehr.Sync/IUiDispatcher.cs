namespace Feuerwehr.Sync;

/// <summary>
/// Marshals work onto the app's UI thread. Network changes arrive on background threads — SignalR's
/// receive loop on a joined client (<see cref="RemoteIncidentSession"/>), a Kestrel request thread on
/// a host — but they ultimately drive ViewModels that mutate Avalonia-bound <c>ObservableCollection</c>s,
/// which Avalonia only accepts on the UI thread. Raising the session's <c>Changed</c> off-thread leaves
/// the view stale, so both background sources funnel their notifications through this seam.
///
/// It is the thread-boundary counterpart of the <c>IClock</c>/<c>ITicker</c> testability seams: each
/// platform head implements it over its real dispatcher (desktop/Android: Avalonia's
/// <c>Dispatcher.UIThread</c>); tests and single-threaded callers use <see cref="ImmediateUiDispatcher"/>.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. Implementations run it inline when the caller
    /// is already on the UI thread, so a local (already-on-thread) notification keeps today's
    /// synchronous ordering and only a genuine cross-thread call is queued.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Runs <paramref name="func"/> on the UI thread and completes with its result. Awaitable from a
    /// background thread — the host uses it to apply a client's command (and read back the resulting
    /// snapshot) on the UI thread, so the authoritative model is only ever touched there.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}

/// <summary>
/// An <see cref="IUiDispatcher"/> that runs everything inline on the calling thread. Correct only when
/// the caller is already the single/UI thread — which is the case for tests (no separate UI thread) and
/// any single-threaded host. Production Avalonia heads supply a real dispatcher instead.
/// </summary>
public sealed class ImmediateUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
    public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
}
