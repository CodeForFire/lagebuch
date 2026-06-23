using Feuerwehr.AppLogic.Services;

namespace Feuerwehr.App.Services;

/// <summary>
/// Temporary no-op ticker for composition root until Task 6 implements DispatcherTimerTicker.
/// </summary>
internal sealed class NoopTicker : ITicker
{
    public IDisposable Subscribe(Action onTick) => new Subscription();

    private sealed class Subscription : IDisposable
    {
        public void Dispose() { }
    }
}
