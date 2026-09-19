using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.Atemschutz;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Trupp-Typen Stammdaten (#398) — rows of name + Stärke + Einsatzzeit. It was a
/// plain <see cref="EditableListSection"/> of names while the crew size and Einsatzzeit were
/// decided by string-comparing those names against compiled-in literals; carrying the numbers on
/// the row is what stops a rename from silently dropping the three-person rule.
/// </summary>
public sealed partial class TruppTypesSection : EditorSection
{
    private readonly Action _onChanged;

    public TruppTypesSection(string title, IEnumerable<TruppType> truppTypes, Action onChanged)
        : base(title)
    {
        _onChanged = onChanged;
        Rows = new ObservableCollection<TruppTypeRow>(
            truppTypes.Select(t => NewRow(t.Name, t.MemberCount, t.MaxDurationMinutes)));
    }

    public ObservableCollection<TruppTypeRow> Rows { get; }

    private TruppTypeRow NewRow(string name, int memberCount, int maxDurationMinutes) =>
        new(name, memberCount, maxDurationMinutes, _onChanged);

    [RelayCommand]
    private void Add()
    {
        Rows.Add(NewRow(
            string.Empty, AtemschutzTrupp.StandardMemberCount, AtemschutzTrupp.DefaultMaxDurationMinutes));
        _onChanged();
    }

    [RelayCommand]
    private void Remove(TruppTypeRow row)
    {
        if (Rows.Remove(row))
        {
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveUp(TruppTypeRow row)
    {
        var i = Rows.IndexOf(row);
        if (i > 0)
        {
            Rows.Move(i, i - 1);
            _onChanged();
        }
    }

    [RelayCommand]
    private void MoveDown(TruppTypeRow row)
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
    /// the same normalization the plain name list did before, since the Atemschutz form looks a
    /// type up by name and two rows differing only in case would make that lookup a coin toss.
    /// The crew size is clamped to what a Trupp can actually have.
    /// </summary>
    public IReadOnlyList<TruppType> ToValues()
    {
        var result = new List<TruppType>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in Rows)
        {
            var name = row.Name?.Trim() ?? string.Empty;
            if (name.Length > 0 && seen.Add(name))
            {
                result.Add(new TruppType(
                    name, TruppType.ClampMemberCount(row.MemberCount), row.MaxDurationMinutes));
            }
        }

        return result;
    }
}
