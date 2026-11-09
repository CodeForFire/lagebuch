using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the place names of the alarm: rows of the name the Leitstelle writes and the short
/// form the Funkrufnamen use. Their order means nothing (the longest name is tried first), so the
/// rows cannot be moved.
/// </summary>
public sealed partial class DispatchPlaceNamesSection : EditorSection
{
    private readonly Action _onChanged;

    public DispatchPlaceNamesSection(string title, IReadOnlyList<DispatchPlaceName> placeNames, Action onChanged, Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        ArgumentNullException.ThrowIfNull(placeNames);
        _onChanged = onChanged;
        Rows = new ObservableCollection<DispatchPlaceNameRow>(
            placeNames.Select(p => new DispatchPlaceNameRow(p.Name, p.Abbreviation, onChanged)));
    }

    public ObservableCollection<DispatchPlaceNameRow> Rows { get; }

    [RelayCommand]
    private void Add()
    {
        var row = new DispatchPlaceNameRow(string.Empty, string.Empty, _onChanged);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(DispatchPlaceNameRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, AllBlank(row.Name, row.Abbreviation), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>Rows with both a place name and a short form, trimmed, in current order.</summary>
    public IReadOnlyList<DispatchPlaceName> ToValues()
    {
        var result = new List<DispatchPlaceName>();
        foreach (var row in Rows)
        {
            var name = row.Name.Trim();
            var shortForm = row.Abbreviation.Trim();
            if (name.Length > 0 && shortForm.Length > 0)
            {
                result.Add(new DispatchPlaceName(name, shortForm));
            }
        }

        return result;
    }
}
