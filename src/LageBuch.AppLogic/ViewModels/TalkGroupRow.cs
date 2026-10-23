using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>One editable Funkgruppe: name, optional number, TMO or DMO, and an optional note.</summary>
public sealed partial class TalkGroupRow : ObservableObject
{
    private readonly Action _onChanged;

    public TalkGroupRow(string name, string number, TalkGroupKind kind, string note, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _number = number;
        _kind = kind;
        _note = note;
    }

    /// <summary>TMO and DMO with their labels, built once by <see cref="TalkGroupsSection.KindChoices"/>.</summary>
    public IReadOnlyList<TalkGroupKindOption> KindOptions { get; } = TalkGroupsSection.KindChoices;

    [ObservableProperty]
    private string _name;
    [ObservableProperty]
    private string _number;
    [ObservableProperty]
    private TalkGroupKind _kind;
    [ObservableProperty]
    private string _note;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnNumberChanged(string value) => _onChanged();

    partial void OnKindChanged(TalkGroupKind value) => _onChanged();

    partial void OnNoteChanged(string value) => _onChanged();
}

/// <summary>A <see cref="TalkGroupKind"/> paired with its label for the picker.</summary>
public readonly record struct TalkGroupKindOption(TalkGroupKind Value, string Label);
