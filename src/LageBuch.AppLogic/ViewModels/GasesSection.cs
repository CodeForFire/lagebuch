using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Messgase Stammdaten (#410): the gases a crew measures in the Gasmessung, at most
/// <see cref="GasDefinition.MaxCount"/>, in the order every Einsatz will show them. The list never
/// goes empty -- the last row cannot be removed -- because an empty list reads back as CO alone,
/// which would make a deletion silently undo itself. Anything that would not survive the import's
/// rules (a nameless row, thresholds out of order, a name used twice) is named by
/// <see cref="Conflicts"/> and blocks SPEICHERN rather than being dropped. The section says so
/// itself in <see cref="ConflictMessage"/> and marks the rows concerned, rather than leaving it to
/// a badge in the editor's header.
/// </summary>
public sealed partial class GasesSection : EditorSection
{
    private readonly Action _onChanged;

    public GasesSection(string title, IEnumerable<GasDefinition> gases, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        // Every edit, add and remove reports through here, so the marks are current before the
        // editor reads Conflicts for its own badge and SPEICHERN.
        _onChanged = () =>
        {
            RefreshConflicts();
            onChanged();
        };
        Rows = new ObservableCollection<GasRow>(gases.Select(NewRow));
        Rows.CollectionChanged += (_, _) =>
        {
            AddCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
        };
        RefreshConflicts();
    }

    public ObservableCollection<GasRow> Rows { get; }

    /// <summary>What keeps the list from being saved, worded for the section itself, or
    /// <see langword="null"/> when nothing does.</summary>
    [ObservableProperty]
    private string? _conflictMessage;

    internal static IReadOnlyList<GasDirectionOption> DirectionChoices { get; } =
    [
        new(GasDirection.Rising, "steigend"),
        new(GasDirection.Falling, "fallend"),
    ];

    private GasRow NewRow(GasDefinition gas) => new(gas, _onChanged);

    private bool CanAdd => Rows.Count < GasDefinition.MaxCount;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var row = NewRow(new GasDefinition(string.Empty, string.Empty, GasDirection.Rising, null, null, null, null));
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    private bool CanRemove(GasRow? row) => Rows.Count > 1;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove(GasRow? row)
    {
        if (row is null || !Rows.Contains(row) || Rows.Count <= 1)
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, AllBlank(row.Name, row.Unit) && row.Elevated is null && row.Dangerous is null && row.Lethal is null && row.PlausibleUpTo is null && row.UpperAlarm is null, () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>
    /// What keeps the list from being saved, one line per kind of problem, or an empty list. The
    /// rows are numbered from 1 where they have no name to go by.
    /// </summary>
    public IReadOnlyList<string> Conflicts() => Check().Problems;

    // The marks and the message follow every change, so the section names the problem where it
    // is being made (feedback from v0.10.0-beta.1: a header badge alone went unseen).
    private void RefreshConflicts()
    {
        var (problems, rows) = Check();
        foreach (var row in Rows)
        {
            row.HasConflict = rows.Contains(row);
        }

        ConflictMessage = problems.Count == 0 ? null : string.Join("\n", problems.Append("Speichern erst nach Korrektur möglich"));
    }

    private (List<string> Problems, HashSet<GasRow> Rows) Check()
    {
        var problems = new List<string>();
        var rows = new HashSet<GasRow>();
        var unnamed = Rows
            .Select((row, index) => (Row: row, Number: index + 1))
            .Where(x => string.IsNullOrWhiteSpace(x.Row.Name))
            .ToList();
        if (unnamed.Count > 0)
        {
            rows.UnionWith(unnamed.Select(x => x.Row));
            var numbers = unnamed.Select(x => x.Number.ToString(CultureInfo.InvariantCulture)).ToList();
            problems.Add(numbers.Count == 1 ? $"Messgas ohne Namen: Zeile {numbers[0]}" : $"Messgas ohne Namen: Zeilen {string.Join(", ", numbers)}");
        }

        var duplicates = Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .GroupBy(r => r.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .ToList();
        if (duplicates.Count > 0)
        {
            rows.UnionWith(duplicates.SelectMany(g => g));
            problems.Add(duplicates.Count == 1
                ? $"Messgas {duplicates[0].Key} ist doppelt eingetragen"
                : $"Messgase {string.Join(", ", duplicates.Select(g => g.Key))} sind doppelt eingetragen");
        }

        var disordered = Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !r.ToDefinition().ThresholdsInOrder)
            .ToList();
        if (disordered.Count > 0)
        {
            rows.UnionWith(disordered);
            problems.Add($"Schwellen nicht in Reihenfolge: {string.Join(", ", disordered.Select(r => r.Name.Trim()))}");
        }

        return (problems, rows);
    }

    /// <summary>The rows as the Stammdaten keep them: sanitised the way the import is, so a saved
    /// list always passes it. Never empty.</summary>
    public IReadOnlyList<GasDefinition> ToValues()
    {
        var gases = GasDefinition.SanitizeList(Rows.Select(r => r.ToDefinition()));
        return gases.Count > 0 ? gases : [GasDefinition.DefaultCo];
    }
}
