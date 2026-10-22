using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// Editor for the Straßen Stammdaten list (#461) -- rows of Straße + Ortsteil + PLZ + Ort, offered
/// in the Einsatzdaten dialog. A list generated from OpenStreetMap runs to hundreds of rows, which
/// is why the view builds only the rows on screen, as it does for Fahrzeuge.
/// </summary>
public sealed partial class StreetsSection : EditorSection
{
    private readonly Action _onChanged;
    private readonly IReadOnlyList<string> _districtOptions;

    public StreetsSection(
        string title,
        IEnumerable<Street> streets,
        IReadOnlyList<string> districtOptions,
        Action onChanged,
        Action<string, Action>? requestConfirm = null)
        : base(title, requestConfirm)
    {
        _onChanged = onChanged;
        _districtOptions = districtOptions;
        Rows = new ObservableCollection<StreetRow>(
            streets.Select(s => NewRow(s.Name, s.District, s.PostalCode, s.Place)));
    }

    public ObservableCollection<StreetRow> Rows { get; }

    private StreetRow NewRow(string name, string district, string postalCode, string place) =>
        new(name, district, postalCode, place, _districtOptions, _onChanged);

    [RelayCommand]
    private void Add()
    {
        var row = NewRow(string.Empty, string.Empty, string.Empty, string.Empty);
        Rows.Add(row);
        _onChanged();
        OnRowAdded(row);
    }

    [RelayCommand]
    private void Remove(StreetRow row)
    {
        if (!Rows.Contains(row))
        {
            return;
        }

        RemoveAfterConfirm(row, row.Name, AllBlank(row.Name, row.District, row.PostalCode, row.Place), () =>
        {
            if (Rows.Remove(row))
            {
                _onChanged();
            }
        });
    }

    /// <summary>Rows with a non-blank name; every field trimmed.</summary>
    public IReadOnlyList<Street> ToValues() =>
        Rows.Select(r => new Street(
                r.Name?.Trim() ?? string.Empty,
                r.District?.Trim() ?? string.Empty,
                r.PostalCode?.Trim() ?? string.Empty,
                r.Place?.Trim() ?? string.Empty))
            .Where(s => s.Name.Length > 0)
            .ToList();
}
