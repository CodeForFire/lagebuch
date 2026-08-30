using Avalonia.Threading;
using LageBuch.Sync;

namespace LageBuch.App.Shared.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> backed by Avalonia's <see cref="Dispatcher.UIThread"/> — the seam the
/// sync layer uses to move host broadcasts (client) and applied client commands (host) back onto the
/// UI thread before they touch bound collections. Shared by the desktop and Android heads.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    // Run inline when already on the UI thread so a local notification keeps its synchronous ordering;
    // only a genuine cross-thread call (a SignalR/Kestrel callback) is queued onto the dispatcher.
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
}
