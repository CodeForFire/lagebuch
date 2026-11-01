using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Funkgruppen Stammdaten: rows of name, number, TMO/DMO and a note. A row without
/// a name is dropped on save; the first spelling wins a case-insensitive duplicate of kind, name and number.
/// <para>
/// The rows run favourites first, then alphabetically (#833), the order every picker lists them in.
/// Like the Geräte (#830) they are sorted when the section is built -- on opening, SPEICHERN,
/// Verwerfen and Import alike -- and not while a row is edited: a row moving under the cursor would
/// take the focus with it, mid-Tab (#773). A new favourite moves to the top on SPEICHERN.
/// </para>
/// </summary>
public sealed partial class TalkGroupsSection : EditorSection
{
    private readonly Action _onChanged;

    public TalkGroupsSection(string title, IEnumerable<TalkGroup> groups, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<TalkGroupRow>(
            TalkGroup.FavouritesFirst(groups, EditorLabel)
                .Select(g => NewRow(g.Name, g.Number ?? string.Empty, g.Kind, g.Note ?? string.Empty, g.IsFavorite)));
    }

    public ObservableCollection<TalkGroupRow> Rows { get; }

    internal static IReadOnlyList<TalkGroupKindOption> KindChoices { get; } =
        Enum.GetValues<TalkGroupKind>()
            .Select(k => new TalkGroupKindOption(k, TalkGroup.KindLabel(k)))
            .ToArray();

    // The row shows name and number in columns of their own, so those two decide its place.
    private static string EditorLabel(TalkGroup g) => $"{g.Name.Trim()} {g.Number?.Trim()}";

    private TalkGroupRow NewRow(string name, string number, TalkGroupKind kind, string note, bool isFavorite) =>
        new(name, number, kind, note, isFavorite, _onChanged);

    [RelayCommand]
    private void Add()
    {
        var row = NewRow(string.Empty, string.Empty, TalkGroupKind.Tmo, string.Empty, isFavorite: false);
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

    /// <summary>
    /// Rows with a non-blank name, trimmed; blank number and note become null. Favourites first, then
    /// alphabetical, so the store and the JSON export hold them in the order the editor shows.
    /// </summary>
    public IReadOnlyList<TalkGroup> ToValues()
    {
        var result = new List<TalkGroup>();
        var seen = new HashSet<(TalkGroupKind, string, string)>();
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            if (name.Length > TalkGroup.MaxNameLength)
            {
                // The TextBox caps typing; this guards a pasted or programmatic value so a saved
                // file always passes the import's cap.
                name = name[..TalkGroup.MaxNameLength].TrimEnd();
            }

            var number = Blank(row.Number);
            if (name.Length == 0 || !seen.Add((row.Kind, name.ToUpperInvariant(), number?.ToUpperInvariant() ?? string.Empty)))
            {
                continue;
            }

            result.Add(new TalkGroup(name, number, row.Kind, Blank(row.Note), row.IsFavorite));
        }

        return [.. TalkGroup.FavouritesFirst(result, EditorLabel)];
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
