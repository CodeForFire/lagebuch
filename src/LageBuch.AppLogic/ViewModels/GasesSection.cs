using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Messgase Stammdaten (#410): the gases a crew measures in the Gasmessung, at most
/// <see cref="GasDefinition.MaxCount"/>, in the order every Einsatz will show them. The list never
/// goes empty -- the last row cannot be removed -- because an empty list reads back as CO alone,
/// which would make a deletion silently undo itself. Anything that would not survive the import's
/// rules (a nameless row, thresholds out of order, a name used twice) is named by
/// <see cref="Conflicts"/> and blocks SPEICHERN rather than being dropped.
/// </summary>
public sealed partial class GasesSection : EditorSection
{
    private readonly Action _onChanged;

    public GasesSection(string title, IEnumerable<GasDefinition> gases, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<GasRow>(gases.Select(NewRow));
        Rows.CollectionChanged += (_, _) =>
        {
            AddCommand.NotifyCanExecuteChanged();
            RemoveCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<GasRow> Rows { get; }

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

        RemoveAfterConfirm(row, row.Name, AllBlank(row.Name, row.Unit) && row.Elevated is null && row.Dangerous is null && row.Lethal is null && row.PlausibleUpTo is null, () =>
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
    public IReadOnlyList<string> Conflicts()
    {
        var problems = new List<string>();
        var unnamed = Rows
            .Select((row, index) => (Row: row, Number: index + 1))
            .Where(x => string.IsNullOrWhiteSpace(x.Row.Name))
            .Select(x => x.Number.ToString(CultureInfo.InvariantCulture))
            .ToList();
        if (unnamed.Count > 0)
        {
            problems.Add(unnamed.Count == 1 ? $"Messgas ohne Namen: Zeile {unnamed[0]}" : $"Messgas ohne Namen: Zeilen {string.Join(", ", unnamed)}");
        }

        var duplicates = Rows
            .Select(r => r.Name?.Trim() ?? string.Empty)
            .Where(n => n.Length > 0)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            problems.Add($"Doppelte Messgase: {string.Join(", ", duplicates)}");
        }

        var disordered = Rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Name) && !r.ToDefinition().ThresholdsInOrder)
            .Select(r => r.Name.Trim())
            .ToList();
        if (disordered.Count > 0)
        {
            problems.Add($"Schwellen nicht in Reihenfolge: {string.Join(", ", disordered)}");
        }

        return problems;
    }

    /// <summary>The rows as the Stammdaten keep them: sanitised the way the import is, so a saved
    /// list always passes it. Never empty.</summary>
    public IReadOnlyList<GasDefinition> ToValues()
    {
        var gases = GasDefinition.SanitizeList(Rows.Select(r => r.ToDefinition()));
        return gases.Count > 0 ? gases : [GasDefinition.DefaultCo];
    }
}
