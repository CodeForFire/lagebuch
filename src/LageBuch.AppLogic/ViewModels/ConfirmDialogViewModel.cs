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

    public ConfirmDialogViewModel(
        string title,
        string message,
        string confirmLabel,
        Action onConfirm,
        string? optionLabel = null,
        string? subOptionLabel = null)
    {
        Title = title;
        Message = message;
        ConfirmLabel = confirmLabel;
        _onConfirm = onConfirm;
        OptionLabel = optionLabel;
        SubOptionLabel = subOptionLabel;
    }

    public string Title { get; }

    public string Message { get; }

    public string ConfirmLabel { get; }

    /// <summary>An optional follow-up offered as a checkbox; the host reads <see cref="IsOptionChecked"/> on confirm.</summary>
    public string? OptionLabel { get; }

    public bool HasOption => OptionLabel is not null;

    // Opt-in: a follow-up the Lagebuchführer did not ask for must never run just because they confirmed.
    [ObservableProperty]
    private bool _isOptionChecked;

    /// <summary>
    /// A refinement of the option, offered as a nested checkbox that is only enabled while the
    /// option is ticked; the host reads <see cref="IsSubOptionChecked"/> on confirm.
    /// </summary>
    public string? SubOptionLabel { get; }

    public bool HasSubOption => SubOptionLabel is not null;

    [ObservableProperty]
    private bool _isSubOptionChecked;

    /// <summary>Raised after Confirm or Cancel so the host removes the overlay.</summary>
    public event EventHandler? Closed;

    // The sub-option only means something under the option, so it never outlives it — a tick
    // left on a disabled checkbox must not fire.
    partial void OnIsOptionCheckedChanged(bool value)
    {
        if (!value)
        {
            IsSubOptionChecked = false;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        _onConfirm();
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);
}
