using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain.ValueObjects;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The "Einsatzdaten" overlay behind the workspace header's edit affordance: Stichwort,
/// Einsatznummer, Straße and Ortsteil edited together, buffered until SPEICHERN (the CO-Messung
/// editor's contract, #242). It replaces the inline Einsatznummer editor (#69) -- the number is
/// still typically unknown at creation and added once ILS calls back, but it is now one field of
/// several rather than the only one editable after the fact; the address in particular had no UI
/// at all before, so the PDF's "Adresse" line was always empty. Closed fires on Speichern AND
/// Abbrechen so the host clears the overlay regardless of outcome (ConfirmDialogViewModel contract).
/// </summary>
public sealed partial class IncidentDataDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;

    public IncidentDataDialogViewModel(IIncidentSession session, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _onChanged = onChanged;

        var incident = session.Incident;
        _keyword = incident.Keyword;
        _incidentNumber = incident.IncidentNumber?.Value;
        _street = incident.Street;
        _district = incident.District;
    }

    public event EventHandler? Closed;

    [ObservableProperty]
    private string? _keyword;

    [ObservableProperty]
    private string? _incidentNumber;

    [ObservableProperty]
    private string? _street;

    [ObservableProperty]
    private string? _district;

    private bool CanSave => !_session.IsReadOnly;

    // Each setter is only invoked when its value actually changed: every call is a full save on a
    // local session and a broadcast command on a joined one, so a no-op SPEICHERN must not fan
    // out into three of either.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var incident = _session.Incident;
        var keyword = Normalize(Keyword);
        var number = Normalize(IncidentNumber);
        var street = Normalize(Street);
        var district = Normalize(District);

        var changed = false;
        if (keyword != incident.Keyword)
        {
            _session.SetKeyword(keyword);
            changed = true;
        }

        if (number != incident.IncidentNumber?.Value)
        {
            _session.SetIncidentNumber(number is null ? null : new IncidentNumber(number));
            changed = true;
        }

        if (street != incident.Street || district != incident.District)
        {
            _session.SetAddress(street, district);
            changed = true;
        }

        if (changed)
        {
            _onChanged();
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
