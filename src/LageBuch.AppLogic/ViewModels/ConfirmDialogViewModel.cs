using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A small reusable yes/no overlay for guarding destructive actions. The host supplies the
/// text and a callback to run on confirmation; the dialog raises <see cref="Closed"/> so the
/// host can clear the overlay regardless of outcome.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ObservableObject
{
    private readonly Action _onConfirm;

    public ConfirmDialogViewModel(string title, string message, string confirmLabel, Action onConfirm)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        _onConfirm = onConfirm;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }

    /// <summary>Raised after Confirm or Cancel so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    [RelayCommand]
    private void Confirm()
    {
        _onConfirm();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);
}
