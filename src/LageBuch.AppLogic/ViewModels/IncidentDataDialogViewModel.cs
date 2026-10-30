using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.MasterData;
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
    private readonly Action? _onAddressSaved;
    private readonly IReadOnlyList<Street> _streets;
    private readonly IncidentNumberFormat? _incidentNumberFormat;

    // What the last recognised street filled in (#461), per field. A field still holding exactly
    // that may be refilled by the next street; anything else was typed and is never touched.
    private string? _filledDistrict;
    private string? _filledPostalCode;
    private string? _filledPlace;

    public IncidentDataDialogViewModel(
        IIncidentSession session,
        Action onChanged,
        IReadOnlyList<string>? keywordOptions = null,
        IReadOnlyList<string>? districts = null,
        IReadOnlyList<Street>? streets = null,
        IncidentNumberFormat? incidentNumberFormat = null,
        Action? onAddressSaved = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _onChanged = onChanged;
        _onAddressSaved = onAddressSaved;
        _incidentNumberFormat = incidentNumberFormat;
        KeywordOptions = keywordOptions ?? [];
        _streets = streets ?? [];
        StreetOptions = Distinct(_streets.Select(s => s.Name));
        DistrictOptions = Distinct((districts ?? []).Concat(_streets.Select(s => s.District)));

        var incident = session.Incident;
        _keyword = incident.Keyword;
        _incidentNumber = incident.IncidentNumber?.Value;
        if (_incidentNumber is null && incidentNumberFormat is not null)
        {
            (_incidentNumberPrefill, _incidentNumberCaretIndex) = incidentNumberFormat.Prefill(incident.StartedAt, _keyword);
            _incidentNumber = _incidentNumberPrefill;
        }

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

    /// <summary>
    /// The Stammdaten's Straßen (#461), each name once, as suggestions. Like the Stichwörter they
    /// are never enforced: a street the list lacks is recorded as typed.
    /// </summary>
    public IReadOnlyList<string> StreetOptions { get; }

    /// <summary>
    /// The Stammdaten's Ortsteile, plus any Ortsteil a street names that the list itself lacks.
    /// </summary>
    public IReadOnlyList<string> DistrictOptions { get; }

    [ObservableProperty]
    private string? _keyword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIncidentNumberFormatHint))]
    private string? _incidentNumber;

    /// <summary>
    /// What the Stammdaten's Einsatznummer format typed in for an incident that has no number yet
    /// (#462): the Einsatzart from the Stichwort, the spec's version and the date, with the
    /// Leitstelle's running number left as a gap. Null when nothing was pre-filled. It follows the
    /// Stichwort until the number is typed into, and is never stored as it stands.
    /// </summary>
    [ObservableProperty]
    private string? _incidentNumberPrefill;

    /// <summary>Where the first gap in <see cref="IncidentNumberPrefill"/> is, for the caret.</summary>
    [ObservableProperty]
    private int _incidentNumberCaretIndex;

    /// <summary>
    /// A number that does not have the format's shape is only pointed out, never refused: an
    /// überörtliche Leitstelle numbers its own way.
    /// </summary>
    public bool ShowIncidentNumberFormatHint =>
        _incidentNumberFormat is not null
        && NumberToSave is { } number
        && !_incidentNumberFormat.Matches(number);

    public string IncidentNumberFormatHint => $"Entspricht nicht dem Format {_incidentNumberFormat?.Template}";

    // The Einsatzart comes from the Stichwort, which is usually typed in this very dialog, after it
    // opened: refill the number while it still is exactly the pre-fill, and never once typed into.
    partial void OnKeywordChanged(string? value)
    {
        if (_incidentNumberFormat is null
            || IncidentNumberPrefill is null
            || !string.Equals(IncidentNumber, IncidentNumberPrefill, StringComparison.Ordinal))
        {
            return;
        }

        var (text, caret) = _incidentNumberFormat.Prefill(_session.Incident.StartedAt, value);
        IncidentNumberPrefill = text;
        IncidentNumberCaretIndex = caret;
        IncidentNumber = text;
    }

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
                || NumberToSave != incident.IncidentNumber?.Value
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
        var number = NumberToSave;

        var changed = false;
        var addressChanged = false;
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
                StreetToSave,
                DistrictToSave,
                Normalize(HouseNumber),
                Normalize(PostalCode),
                Normalize(Place));
            changed = true;
            addressChanged = true;
        }

        if (changed)
        {
            _onChanged();
        }

        // After the save, so whoever looks the address up reads the new one.
        if (addressChanged)
        {
            _onAddressSaved?.Invoke();
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

    // The untouched pre-fill is no number at all: SPEICHERN after typing only the Stichwort must not
    // store "B 1.2 260715 " (#462).
    private string? NumberToSave =>
        string.Equals(IncidentNumber, IncidentNumberPrefill, StringComparison.Ordinal) ? null : Normalize(IncidentNumber);

    private string? StreetToSave => Normalize(StammdatenCatalogue.Normalize(Street, StreetOptions));

    private string? DistrictToSave => Normalize(StammdatenCatalogue.Normalize(District, DistrictOptions));

    private bool AddressDiffers(Domain.Incident incident) =>
        StreetToSave != incident.Street
        || Normalize(HouseNumber) != incident.HouseNumber
        || Normalize(PostalCode) != incident.PostalCode
        || Normalize(Place) != incident.Place
        || DistrictToSave != incident.District;

    /// <summary>
    /// A recognised street fills in its Ortsteil, PLZ and Ort (#461) -- each only where every
    /// Stammdaten row of that name agrees, and only into a field that is empty or still holds what
    /// the previous street filled in. Text that matches no street changes nothing, so typing on
    /// past a name ("Hauptstraße 12") keeps what it brought.
    /// </summary>
    partial void OnStreetChanged(string? value)
    {
        var name = StammdatenCatalogue.Normalize(value, StreetOptions);
        var rows = _streets.Where(s => string.Equals(s.Name.Trim(), name, StringComparison.Ordinal)).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        District = Fill(District, ref _filledDistrict, Agreed(rows, s => s.District));
        PostalCode = Fill(PostalCode, ref _filledPostalCode, Agreed(rows, s => s.PostalCode));
        Place = Fill(Place, ref _filledPlace, Agreed(rows, s => s.Place));
    }

    // The one non-blank value every row holds, or null when a row is blank or two rows differ.
    private static string? Agreed(List<Street> rows, Func<Street, string> field)
    {
        var values = rows.Select(r => field(r).Trim()).Distinct(StringComparer.Ordinal).ToList();
        return values is [{ Length: > 0 } only] ? only : null;
    }

    private static string? Fill(string? current, ref string? filled, string? value)
    {
        if (!string.IsNullOrWhiteSpace(current) && !string.Equals(current, filled, StringComparison.Ordinal))
        {
            return current;
        }

        filled = value;
        return value;
    }

    private static List<string> Distinct(IEnumerable<string> values) =>
        values.Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
