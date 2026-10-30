using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One TMO or DMO field of an Einsatzleitung (#828) in <see cref="CommandNodeDialogViewModel"/>:
/// free text with the Stammdaten Funkgruppen of its kind as suggestions. The dialog owns the list
/// and decides when a slot may go.
/// </summary>
public sealed partial class FunkgruppeSlot : ObservableObject
{
    private readonly Action<FunkgruppeSlot> _onRemove;
    private readonly Func<bool> _canRemove;

    [ObservableProperty]
    private string? _text;

    internal FunkgruppeSlot(
        string kind, string? text, IReadOnlyList<string> options, Action<FunkgruppeSlot> onRemove, Func<bool> canRemove)
    {
        Kind = kind;
        _text = text;
        Options = options;
        _onRemove = onRemove;
        _canRemove = canRemove;
    }

    /// <summary>"TMO" or "DMO": what the field is, for its automation name.</summary>
    public string Kind { get; }

    /// <summary>The field's automation name, e.g. "Funkgruppe TMO".</summary>
    public string AccessibleName => $"Funkgruppe {Kind}";

    /// <summary>An example of the kind's naming, shown while the field is empty.</summary>
    public string Placeholder => Kind == "DMO" ? "z. B. 301_F" : "z. B. FW_MUSTER 1000";

    /// <summary>The Stammdaten Funkgruppen of this kind, offered as suggestions and never enforced.</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>Whether this field can be taken away: one of each kind always stays.</summary>
    public bool CanRemove => _canRemove();

    /// <summary>Takes this field away; its text goes with it.</summary>
    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => _onRemove(this);

    /// <summary>Re-reads <see cref="CanRemove"/> after the list around it changed.</summary>
    internal void Refresh()
    {
        OnPropertyChanged(nameof(CanRemove));
        RemoveCommand.NotifyCanExecuteChanged();
    }
}
