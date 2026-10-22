using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.ValueObjects;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The "Einsatzdaten" overlay behind the workspace header's edit affordance: Stichwort,
/// Einsatznummer and the address (Straße, Hausnummer, PLZ, Ort, Ortsteil; #401) edited together,
/// buffered until SPEICHERN (the CO-Messung editor's contract, #242). It replaces the inline Einsatznummer editor (#69) -- the number is
/// still typically unknown at creation and added once ILS calls back, but it is now one field of
/// several rather than the only one editable after the fact; the address in particular had no UI
/// at all before, so the PDF's "Adresse" line was always empty. Closed fires on Speichern AND
/// Abbrechen so the host clears the overlay regardless of outcome (ConfirmDialogViewModel contract).
/// </summary>
public sealed partial class IncidentDataDialogViewModel : ObservableObject
{
    private readonly IIncidentSession _session;
    private readonly Action _onChanged;

    public IncidentDataDialogViewModel(IIncidentSession session, Action onChanged, IReadOnlyList<string>? keywordOptions = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _onChanged = onChanged;
        KeywordOptions = keywordOptions ?? [];

        var incident = session.Incident;
        _keyword = incident.Keyword;
        _incidentNumber = incident.IncidentNumber?.Value;
        _street = incident.Street;
        _houseNumber = incident.HouseNumber;
        _postalCode = incident.PostalCode;
        _place = incident.Place;
        _district = incident.District;
    }

    public event EventHandler? Closed;

    /// <summary>
    /// The Stammdaten's Stichwörter (#704), offered as suggestions and never enforced: the alarm
    /// text is what it is, and only the chosen text is stored in the incident.
    /// </summary>
    public IReadOnlyList<string> KeywordOptions { get; }

    [ObservableProperty]
    private string? _keyword;

    [ObservableProperty]
    private string? _incidentNumber;

    [ObservableProperty]
    private string? _street;

    [ObservableProperty]
    private string? _houseNumber;

    [ObservableProperty]
    private string? _postalCode;

    [ObservableProperty]
    private string? _place;

    [ObservableProperty]
    private string? _district;

    /// <summary>
    /// Set while ABBRECHEN asks whether typed Einsatzdaten may go (#652). SPEICHERN waits for the
    /// answer, so Enter in a field cannot save past the question.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDiscardConfirmOpen;

    /// <summary>
    /// Whether any field differs from the incident as SPEICHERN would see it, against the live
    /// incident rather than the values the dialog opened with, as SPEICHERN does.
    /// </summary>
    public bool HasChanges
    {
        get
        {
            var incident = _session.Incident;
            return KeywordToSave != incident.Keyword
                || Normalize(IncidentNumber) != incident.IncidentNumber?.Value
                || AddressDiffers(incident);
        }
    }

    private bool CanSave => !_session.IsReadOnly && !IsDiscardConfirmOpen;

    // Each setter is only invoked when its value actually changed: every call is a full save on a
    // local session and a broadcast command on a joined one, so a no-op SPEICHERN must not fan
    // out into three of either.
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        var incident = _session.Incident;
        var keyword = KeywordToSave;
        var number = Normalize(IncidentNumber);

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

        if (AddressDiffers(incident))
        {
            _session.SetAddress(
                Normalize(Street),
                Normalize(District),
                Normalize(HouseNumber),
                Normalize(PostalCode),
                Normalize(Place));
            changed = true;
        }

        if (changed)
        {
            _onChanged();
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ABBRECHEN, and Esc: closes at once when nothing was typed, asks first otherwise.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (HasChanges)
        {
            IsDiscardConfirmOpen = true;
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ConfirmDiscard()
    {
        IsDiscardConfirmOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CancelDiscard() => IsDiscardConfirmOpen = false;

    // In the Stammdaten's spelling when it matches one apart from case and whitespace, so "b3p"
    // records as "B3P"; otherwise exactly as typed.
    private string? KeywordToSave => Normalize(StammdatenCatalogue.Normalize(Keyword, KeywordOptions));

    private bool AddressDiffers(Domain.Incident incident) =>
        Normalize(Street) != incident.Street
        || Normalize(HouseNumber) != incident.HouseNumber
        || Normalize(PostalCode) != incident.PostalCode
        || Normalize(Place) != incident.Place
        || Normalize(District) != incident.District;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
