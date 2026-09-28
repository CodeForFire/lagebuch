using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Funktionen Stammdaten (#470) — rows of name + how often the Funktion may be held.
/// It was a plain <see cref="EditableListSection"/> of names while nothing carried the mode, so a
/// brigade had nowhere to say that an EL is one person per Einsatz, and a save quietly reset every
/// Funktion to mehrfach.
/// </summary>
public sealed partial class RolesSection : EditorSection
{
    private readonly Action _onChanged;

    public RolesSection(string title, IEnumerable<Role> roles, Action onChanged)
        : base(title)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<RoleRow>(roles.Select(r => NewRow(r.Name, r.Uniqueness)));
    }

    public ObservableCollection<RoleRow> Rows { get; }

    /// <summary>
    /// The picker's options, in the order the enum declares them: mehrfach first, because that is
    /// what a Funktion nobody constrained is, and the two constrained modes after it. Built once
    /// and shared, since <see cref="RoleRow.UniquenessOptions"/> offers every row the same three.
    /// <para>
    /// The labels live here and not next to the other enum labels in <c>Domain/Formatting.cs</c>:
    /// <see cref="RoleUniqueness"/> belongs to LageBuch.Persistence, and Domain references nothing,
    /// so a mapping there would invert the dependency. AppLogic references both, so it is the one
    /// place the two can meet. <c>ImportanceOption</c> gets away without the argument only because
    /// <c>TaskImportance</c> is itself a Domain type.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<UniquenessOption> UniquenessChoices { get; } =
        Enum.GetValues<RoleUniqueness>()
            .Select(v => new UniquenessOption(v, Label(v)))
            .ToArray();

    private static string Label(RoleUniqueness uniqueness) => uniqueness switch
    {
        RoleUniqueness.UniquePerIncident => "einmal je Einsatz",
        RoleUniqueness.UniquePerSection => "einmal je Abschnitt",
        _ => "mehrfach",
    };

    private RoleRow NewRow(string name, RoleUniqueness uniqueness) =>
        new(name, uniqueness, _onChanged);

    [RelayCommand]
    private void Add()
    {
        Rows.Add(NewRow(string.Empty, RoleUniqueness.Multiple));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(RoleRow row)
    {
        if (Rows.Remove(row))
        {
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveUp(RoleRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(RoleRow row)
    {
        var i = Rows.IndexOf(row);
        if (i >= 0 && i < Rows.Count - 1)
        {
            Rows.Move(i, i + 1);
            _onChanged();
        }
    }

    /// <summary>
    /// Rows with a non-blank name, trimmed, first spelling winning a case-insensitive duplicate --
    /// the same normalization <see cref="TruppTypesSection"/> uses, and deliberately stricter than
    /// the <see cref="EditableListSection"/> this replaced: that one compared ordinally, so "EL" and
    /// "el" survived as two Funktionen, although <c>StammdatenCatalogue</c> has always matched them
    /// as one and the Funktionen tab offers either as a suggestion. A duplicate dropped this way
    /// takes its first row's mode with it, so the row the brigade actually configured is the one
    /// that survives.
    /// </summary>
    public IReadOnlyList<Role> ToValues()
    {
        var result = new List<Role>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            if (name.Length > 0 && seen.Add(name))
            {
                result.Add(new Role(name, row.Uniqueness));
            }
        }

        return result;
    }
}
