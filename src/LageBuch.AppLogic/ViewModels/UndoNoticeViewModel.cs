using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A short-lived "Rückgängig" offer after a toggle (#543): ticking off an Aufgabe takes one
/// stray Space, so the change is offered back for a few seconds. The host decides when it
/// expires and clears it on <see cref="Closed"/>, whichever way the notice ended.
/// </summary>
public sealed partial class UndoNoticeViewModel : ObservableObject
{
    private readonly Action _undo;

    public UndoNoticeViewModel(string message, Action undo, DateTimeOffset expiresAt)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(undo);
        Message = message;
        _undo = undo;
        ExpiresAt = expiresAt;
    }

    public string Message { get; }

    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Raised after Undo or Dismiss so the host removes the notice.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Undo()
    {
        _undo();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Dismiss() => Closed?.Invoke(this, EventArgs.Empty);
}
