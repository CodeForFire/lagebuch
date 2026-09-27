using CommunityToolkit.Mvvm.ComponentModel;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One editable Funktion: its name, and how often it may be held (#470). The mode used to have
/// nowhere to live -- the Stammdaten offered a flat list of names, so a brigade had no way to say
/// that an Einsatzleiter is one person per Einsatz, and the editor reset every Funktion to mehrfach
/// the moment anybody saved it.
/// </summary>
public sealed partial class RoleRow : ObservableObject
{
    private readonly Action _onChanged;

    public RoleRow(string name, RoleUniqueness uniqueness, Action onChanged)
    {
        _onChanged = onChanged;
        _name = name;
        _uniqueness = uniqueness;
    }

    /// <summary>
    /// The three modes as the picker offers them, each with its German label. One list, built once
    /// by <see cref="RolesSection.UniquenessChoices"/> and handed to every row: an instance property
    /// because that is what a compiled <c>{Binding UniquenessOptions}</c> resolves against, but not
    /// a copy per row.
    /// </summary>
    public IReadOnlyList<UniquenessOption> UniquenessOptions { get; } = RolesSection.UniquenessChoices;

    [ObservableProperty]
    private string _name;

    /// <summary>
    /// How often this Funktion may be held. Everything but mehrfach is advisory at the moment of
    /// entry: the Funktionen tab offers the Übergabe when the slot is already taken.
    /// </summary>
    [ObservableProperty]
    private RoleUniqueness _uniqueness;

    partial void OnNameChanged(string value) => _onChanged();

    partial void OnUniquenessChanged(RoleUniqueness value) => _onChanged();
}

/// <summary>A <see cref="RoleUniqueness"/> paired with its German label (ImportanceOption
/// precedent). A closed record rather than a generic one, so the picker's compiled-binding item
/// template stays simple.</summary>
public readonly record struct UniquenessOption(RoleUniqueness Value, string Label);
