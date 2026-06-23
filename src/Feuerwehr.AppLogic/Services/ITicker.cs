namespace Feuerwehr.AppLogic.Services;

/// <summary>
/// Periodic UI-thread tick (~once per second) used to refresh time-based view state.
/// The AppLogic-layer equivalent of IClock's testability seam: tests substitute a
/// FakeTicker they fire synchronously; the App layer implements it over a real timer.
/// </summary>
public interface ITicker
{
    /// <summary>Invokes <paramref name="onTick"/> roughly once per second on the UI
    /// thread until the returned subscription is disposed.</summary>
    IDisposable Subscribe(Action onTick);
}
