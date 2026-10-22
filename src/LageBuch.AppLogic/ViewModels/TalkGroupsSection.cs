using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Funkgruppen Stammdaten: rows of name, number, TMO/DMO and a note. A row without
/// a name is dropped on save; the first spelling wins a case-insensitive duplicate of kind and name.
/// </summary>
public sealed partial class TalkGroupsSection : EditorSection
{
    private readonly Action _onChanged;

    public TalkGroupsSection(string title, IEnumerable<TalkGroup> groups, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<TalkGroupRow>(
            groups.Select(g => NewRow(g.Name, g.Number ?? string.Empty, g.Kind, g.Note ?? string.Empty)));
    }

    public ObservableCollection<TalkGroupRow> Rows { get; }

    internal static IReadOnlyList<TalkGroupKindOption> KindChoices { get; } =
        Enum.GetValues<TalkGroupKind>()
            .Select(k => new TalkGroupKindOption(k, TalkGroup.KindLabel(k)))
            .ToArray();

    private TalkGroupRow NewRow(string name, string number, TalkGroupKind kind, string note) =>
        new(name, number, kind, note, _onChanged);

    [RelayCommand]
    private void Add()
    {
        var row = NewRow(string.Empty, string.Empty, TalkGroupKind.Tmo, string.Empty);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(TalkGroupRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, AllBlank(row.Name, row.Number, row.Note), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>Rows with a non-blank name, trimmed; blank number and note become null.</summary>
    public IReadOnlyList<TalkGroup> ToValues()
    {
        var result = new List<TalkGroup>();
        var seen = new HashSet<(TalkGroupKind, string)>();
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            if (name.Length == 0 || !seen.Add((row.Kind, name.ToUpperInvariant())))
            {
                continue;
            }

            result.Add(new TalkGroup(name, Blank(row.Number), row.Kind, Blank(row.Note)));
        }

        return result;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
