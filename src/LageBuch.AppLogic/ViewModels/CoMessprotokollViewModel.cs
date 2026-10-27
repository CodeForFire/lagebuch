using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>Which units the matrix shows. Narrowing to the open or affected ones is how a crew
/// answers "what's left" on a large building without scanning every tile.</summary>
public enum CoUnitFilter
{
    All,
    Open,
    Affected,
}

/// <summary>A tile in the search grid: display only. It carries no write-through to the session --
/// #242: every edit is made in the sidebar buffer (<see cref="DwellingEditorViewModel"/>) and
/// committed by FERTIG. The setters stay public so the open sidebar can mirror its pending state
/// onto the tile (see CoMessprotokollViewModel.ApplyPendingEditToMatrix), which is what makes the
/// door mark update as the crew works without anything being written yet.</summary>
public sealed partial class DwellingCellViewModel : ObservableObject
{
    private readonly Action<Guid, int, int> _onOpenEditor;

    private readonly IReadOnlyList<IncidentGas> _gases;

    public DwellingCellViewModel(
        Dwelling dwelling,
        Building building,
        bool isReadOnly,
        Action<Guid, int, int> onOpenEditor,
        IReadOnlyList<IncidentGas>? gases = null)
    {
        ArgumentNullException.ThrowIfNull(dwelling);
        ArgumentNullException.ThrowIfNull(building);
        _gases = gases ?? [];
        Id = dwelling.Id;
        BuildingId = dwelling.BuildingId;
        FloorOrdinal = dwelling.FloorOrdinal;
        ApartmentNumber = dwelling.ApartmentNumber;
        IsReadOnly = isReadOnly;
        _onOpenEditor = onOpenEditor;
        _status = dwelling.Status;
        _values = dwelling.Values;
        _residentName = dwelling.ResidentName;
        _keyAvailable = dwelling.KeyAvailable;
        _keyNumber = dwelling.KeyNumber;
        _label = CoMeasurementLabels.ApartmentLabel(building, dwelling.FloorOrdinal, dwelling.ApartmentNumber);
        Readings = dwelling.Readings;
        StatusBrush = GetStatusBrush(dwelling.Status);
    }

    public Guid Id { get; }

    public Guid BuildingId { get; }

    public int FloorOrdinal { get; }

    public int ApartmentNumber { get; }

    public bool IsReadOnly { get; }

    [ObservableProperty]
    private DwellingStatus _status;

    /// <summary>The current value of each gas (#410), keyed by the Einsatz's own gas id.</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<Guid, decimal> _values;

    [ObservableProperty]
    private string? _residentName;

    [ObservableProperty]
    private KeyAvailability _keyAvailable;

    [ObservableProperty]
    private string? _keyNumber;

    [ObservableProperty]
    private string _statusBrush;

    /// <summary>#265: every unit's identity now lives on the tile itself, independent of every
    /// other floor's same-numbered unit -- edited in the editor sidebar and persisted, batched
    /// with ResidentName/KeyAvailable, when FERTIG commits (see CoMessprotokollViewModel.ConfirmEditor).</summary>
    [ObservableProperty]
    private string _label;

    /// <summary>Every reading committed for this Wohnung, oldest first. Set once from the domain
    /// and never written back: the pending sidebar value is not a measurement until FERTIG, so
    /// there is deliberately nothing here for ApplyPendingEditToMatrix to preview.</summary>
    public IReadOnlyList<CoReading> Readings { get; }

    /// <summary>"Messreihe: 08:14 120 ppm · 08:41 40 ppm" for the tooltip, empty below two
    /// readings -- with one it only repeats <see cref="CoDisplay"/>, which the tooltip already
    /// carries a line above.</summary>
    public string CoSeriesText => Readings.Count < 2
        ? string.Empty
        : "Messreihe: " + string.Join(" · ", Readings.Select(r => CoMeasurementLabels.ReadingLabel(r, _gases)));

    /// <summary>Every measured gas in list order, each with the band it falls in, for the tooltip
    /// (#410). A CO-only Einsatz has one entry and no gas name.</summary>
    public IReadOnlyList<GasValueRow> GasValueRows => CoMeasurementLabels.ValuesInOrder(_gases, Values)
        .Select(v => GasValueRow.From(v, _gases.Count > 1))
        .ToList();

    /// <summary>Every measured gas as one line, "CO 120 ppm · H2S 3 ppm", or "Kein Messwert".</summary>
    public string CoDisplay => GasValueRows is { Count: > 0 } rows
        ? string.Join(" · ", rows.Select(r => r.Text))
        : "Kein Messwert";

    /// <summary>The reading that decides the tile's colour: the worst gas, a tie going to the gas
    /// earlier in the list, the first measured gas while all are normal (#410).</summary>
    public GasValue? Worst => GasSeverityClassifier.Worst(_gases, Values);

    /// <summary>The tile's readout: the deciding gas, named once the Einsatz measures more than one
    /// ("H2S 12 ppm"), so a CO-only Einsatz reads exactly as before. Empty rather than "Kein
    /// Messwert" when nothing is measured: unmeasured is the resting state of every unit in a fresh
    /// building, so spelling it out on each tile prints the same non-information dozens of times and
    /// buries the values that do exist. The status glyph already says the unit is unsearched.</summary>
    public string CoCompact => Worst is { } w ? CoMeasurementLabels.GasValueLabel(w.Gas, w.Value, _gases.Count > 1) : string.Empty;

    // Severity is independent of Status (search progress): a lethal reading on an unsearched tile
    // must still read as lethal. Drives Classes.elevated/dangerous/lethal on the tile's value
    // TextBlock in CoMessprotokollView.axaml, mirroring ScbaView's Classes.alarm pattern.
    public bool IsCoElevated => Worst?.Severity == CoSeverity.Elevated;

    public bool IsCoDangerous => Worst?.Severity == CoSeverity.Dangerous;

    public bool IsCoLethal => Worst?.Severity == CoSeverity.Lethal;

    /// <summary>Detail the compact tile deliberately drops (resident, key, full status wording),
    /// surfaced on hover so nothing is lost -- the tile carries identity + status + the worst gas
    /// only. One sentence, for a screen reader; the tooltip itself shows <see cref="TooltipHead"/>,
    /// then <see cref="GasValueRows"/> each in its own colour (#410), then <see cref="TooltipTail"/>.</summary>
    public string TileTooltip => string.Join(" · ", TooltipHead, CoDisplay, TooltipTail);

    /// <summary>"Mitte · noch nicht abgesucht".</summary>
    public string TooltipHead => $"{Label} · {CoMeasurementLabels.StatusText(Status)}";

    /// <summary>Bewohner, Schlüssel and the Messreihe, after the gas values.</summary>
    public string TooltipTail
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(ResidentName))
            {
                parts.Add(ResidentName!);
            }

            parts.Add(KeyAvailable switch
            {
                KeyAvailability.Available when !string.IsNullOrWhiteSpace(KeyNumber) => $"Schlüssel Nr. {KeyNumber.Trim()}",
                KeyAvailability.Available => "Schlüssel vorhanden",
                KeyAvailability.NotAvailable => "Kein Schlüssel",
                _ => "Schlüssel unbekannt",
            });

            if (CoSeriesText.Length > 0)
            {
                parts.Add(CoSeriesText);
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>The key glyph, followed by the Schlüsselnummer when one was noted, so the crew
    /// reads off the tile which key goes back where.</summary>
    public string KeyDisplay => KeyAvailable switch
    {
        KeyAvailability.Available when !string.IsNullOrWhiteSpace(KeyNumber) => $"\uD83D\uDD11 {KeyNumber.Trim()}",
        KeyAvailability.Available => "\uD83D\uDD11",
        KeyAvailability.NotAvailable => "\u2716",
        _ => string.Empty,
    };

    // Mirrors the spray-marked "X-code" convention search teams already use on doors: a single
    // slash means the search is under way, a complete X means it's cleared, and a circled X flags
    // a find. Shape carries the same meaning as StatusBrush's color, redundantly, on purpose.
    public string StatusGlyph => Status switch
    {
        DwellingStatus.NotSearched => "\u2571",
        DwellingStatus.Searched => "\u2715",
        DwellingStatus.Affected => "\u2297",
        _ => "\u2571",
    };

    private static string GetStatusBrush(DwellingStatus status) => status switch
    {
        DwellingStatus.NotSearched => "#FFC000",
        DwellingStatus.Searched => "#92D050",
        DwellingStatus.Affected => "#FF0000",
        _ => "#FFC000",
    };

    partial void OnStatusChanged(DwellingStatus value)
    {
        StatusBrush = GetStatusBrush(value);
        OnPropertyChanged(nameof(StatusGlyph));
        NotifyTooltip();
    }

    partial void OnValuesChanged(IReadOnlyDictionary<Guid, decimal> value)
    {
        OnPropertyChanged(nameof(Worst));
        OnPropertyChanged(nameof(GasValueRows));
        OnPropertyChanged(nameof(CoDisplay));
        OnPropertyChanged(nameof(CoCompact));
        NotifyTooltip();
        OnPropertyChanged(nameof(IsCoElevated));
        OnPropertyChanged(nameof(IsCoDangerous));
        OnPropertyChanged(nameof(IsCoLethal));
    }

    partial void OnKeyAvailableChanged(KeyAvailability value)
    {
        OnPropertyChanged(nameof(KeyDisplay));
        NotifyTooltip();
    }

    partial void OnKeyNumberChanged(string? value)
    {
        OnPropertyChanged(nameof(KeyDisplay));
        NotifyTooltip();
    }

    partial void OnResidentNameChanged(string? value) => NotifyTooltip();

    partial void OnLabelChanged(string value) => NotifyTooltip();

    private void NotifyTooltip()
    {
        OnPropertyChanged(nameof(TooltipHead));
        OnPropertyChanged(nameof(TooltipTail));
        OnPropertyChanged(nameof(TileTooltip));
    }

    [RelayCommand]
    private void OpenEditor() => _onOpenEditor(BuildingId, FloorOrdinal, ApartmentNumber);
}

/// <summary>#242: the "WOHNUNG BEARBEITEN" sidebar's edit buffer. Every field is held here until
/// FERTIG, so ABBRECHEN can actually discard -- previously Status and CO-Wert were written straight
/// through on each click, which put every intermediate value (and a mistyped ppm reading) in the
/// Einsatztagebuch before the operator had a chance to cancel. One dwelling is one reportable
/// event, the same reasoning the Kräfte Stärke editor already applies to its three numbers.
/// Holds no session reference on purpose: it cannot write, only be read back on commit.</summary>
/// <summary>
/// One committed reading, pre-formatted for the MESSREIHE list — the view binds strings and
/// booleans, never a domain record (the <c>EtbEntryRow</c> convention).
/// </summary>
public sealed record CoReadingRow(
    string Time,
    string Value,
    string RecordedBy,
    bool IsCoElevated,
    bool IsCoDangerous,
    bool IsCoLethal,
    string Gas = "")
{
    /// <summary>A row for one reading. <see cref="Gas"/> names the gas once the Einsatz measures
    /// more than one (#410), and stays empty otherwise so a CO-only Messreihe reads as before.</summary>
    public static CoReadingRow From(CoReading reading, IReadOnlyList<IncidentGas> gases)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(gases);
        var gas = gases.FirstOrDefault(g => g.Id == reading.GasId);
        var severity = gas is null ? CoSeverity.Normal : GasSeverityClassifier.SeverityOf(gas.Definition, reading.Value);
        return new(
            Formatting.TimeOfDay(reading.MeasuredAt),
            reading.Value is { } v ? Formatting.Measurement(v, gas?.Unit ?? string.Empty) : "gelöscht",
            reading.RecordedBy,
            severity == CoSeverity.Elevated,
            severity == CoSeverity.Dangerous,
            severity == CoSeverity.Lethal,
            gases.Count > 1 && gas is not null ? gas.Name : string.Empty);
    }
}

/// <summary>One gas's current value, pre-formatted for the tooltip and the sidebar (#410), with the
/// band that colours it.</summary>
public sealed record GasValueRow(string Text, bool IsCoElevated, bool IsCoDangerous, bool IsCoLethal)
{
    public static GasValueRow From(GasValue value, bool withName)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(
            CoMeasurementLabels.GasValueLabel(value.Gas, value.Value, withName),
            value.Severity == CoSeverity.Elevated,
            value.Severity == CoSeverity.Dangerous,
            value.Severity == CoSeverity.Lethal);
    }
}

/// <summary>
/// One gas's input in the WOHNUNG BEARBEITEN sidebar (#410): the value as typed, against the value
/// committed, with the band and the plausibility hint recomputed live so the danger colour shows
/// before FERTIG. Like the rest of the sidebar it writes nothing; the editor reads it back on
/// commit.
/// </summary>
public sealed partial class GasInputRow : ObservableObject
{
    private readonly Action _onChanged;

    public GasInputRow(IncidentGas gas, decimal? original, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(gas);
        Gas = gas;
        Original = original;
        _value = original;
        _onChanged = onChanged ?? (() => { });
    }

    public IncidentGas Gas { get; }

    public string Name => Gas.Name;

    public string Unit => Gas.Unit;

    /// <summary>The overline above the input: the gas's name, "CO". Its unit sits inside the field.</summary>
    public string Header => Formatting.Upper(Gas.Name);

    /// <summary>What a screen reader announces for the text-less input.</summary>
    public string AccessibleName => $"{Gas.Name}-Wert";

    public decimal? Original { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChange))]
    [NotifyPropertyChangedFor(nameof(IsCoElevated))]
    [NotifyPropertyChangedFor(nameof(IsCoDangerous))]
    [NotifyPropertyChangedFor(nameof(IsCoLethal))]
    [NotifyPropertyChangedFor(nameof(IsImplausible))]
    private decimal? _value;

    /// <summary>The value FERTIG would record: rounded the way the domain stores it, so typing
    /// 20,90 over 20,9 is not mistaken for a new reading.</summary>
    public decimal? Committed => Value is { } v ? GasDefinition.NormalizeValue(v) ?? v : null;

    public bool HasChange => Committed != Original;

    public bool IsCoElevated => GasSeverityClassifier.SeverityOf(Gas.Definition, Value) == CoSeverity.Elevated;

    public bool IsCoDangerous => GasSeverityClassifier.SeverityOf(Gas.Definition, Value) == CoSeverity.Dangerous;

    public bool IsCoLethal => GasSeverityClassifier.SeverityOf(Gas.Definition, Value) == CoSeverity.Lethal;

    /// <summary>A caution about a likely *typo* (e.g. 9999 vs 999), distinct from severity: a real
    /// fire scene can genuinely produce extreme values, so this never blocks FERTIG -- it only
    /// asks the Lagebuchführer to double-check what they typed.</summary>
    public bool IsImplausible => GasSeverityClassifier.IsImplausible(Gas.Definition, Value);

    partial void OnValueChanged(decimal? value) => _onChanged();
}

public sealed partial class DwellingEditorViewModel : ObservableObject
{
    public DwellingEditorViewModel(
        Dwelling dwelling, Building building, IReadOnlyList<string>? involvedPartyNames = null, IReadOnlyList<IncidentGas>? gases = null)
    {
        ArgumentNullException.ThrowIfNull(dwelling);
        ArgumentNullException.ThrowIfNull(building);
        InvolvedPartyNames = involvedPartyNames ?? [];
        var incidentGases = gases ?? [];
        BuildingId = dwelling.BuildingId;
        FloorOrdinal = dwelling.FloorOrdinal;
        ApartmentNumber = dwelling.ApartmentNumber;

        OriginalStatus = dwelling.Status;
        OriginalResidentName = dwelling.ResidentName;
        OriginalKeyAvailable = dwelling.KeyAvailable;
        OriginalKeyNumber = dwelling.KeyNumber;
        OriginalLabel = CoMeasurementLabels.ApartmentLabel(building, dwelling.FloorOrdinal, dwelling.ApartmentNumber);

        // Newest first: during an Einsatz the question is "what is it now, and was it worse?",
        // so the latest reading belongs at the top where the eye lands.
        Readings = dwelling.Readings.Reverse().Select(r => CoReadingRow.From(r, incidentGases)).ToList();

        // One input per gas of the Einsatz, in list order (#410).
        GasInputs = incidentGases.Select(g => new GasInputRow(g, dwelling.ValueOf(g.Id), OnGasInputChanged)).ToList();

        _status = OriginalStatus;
        _residentName = OriginalResidentName;
        _keyAvailable = OriginalKeyAvailable;
        _keyNumber = OriginalKeyNumber;
        _label = OriginalLabel;
    }

    public Guid BuildingId { get; }

    /// <summary>
    /// The Beteiligten recorded as found here (#702), read-only: the link is made on the Beteiligte
    /// card, where the person's details are, and shown here so the Wohnung tells who was in it.
    /// </summary>
    public IReadOnlyList<string> InvolvedPartyNames { get; }

    public bool HasInvolvedParties => InvolvedPartyNames.Count > 0;

    public int FloorOrdinal { get; }

    public int ApartmentNumber { get; }

    [ObservableProperty]
    private DwellingStatus _status;

    [ObservableProperty]
    private string? _residentName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKeyAvailable))]
    [NotifyPropertyChangedFor(nameof(IsKeyNotAvailable))]
    [NotifyPropertyChangedFor(nameof(IsKeyUnknown))]
    [NotifyPropertyChangedFor(nameof(IsKeyNumberEnabled))]
    private KeyAvailability _keyAvailable;

    /// <summary>The Schlüsselnummer as typed. Kept while the radios move, so a slip to "Keiner"
    /// and back does not lose it; only what FERTIG commits drops it (see Dwelling.WithDetails).</summary>
    [ObservableProperty]
    private string? _keyNumber;

    [ObservableProperty]
    private string _label;

    /// <summary>A Schlüsselnummer only means something for a key the crew actually holds.</summary>
    public bool IsKeyNumberEnabled => KeyAvailable == KeyAvailability.Available;

    // TwoWay radio bindings, as TasksViewModel's filter does: checking writes through to
    // KeyAvailable; writing false (the binding engine syncing the unchecked radios) is a no-op.
    public bool IsKeyAvailable
    {
        get => KeyAvailable == KeyAvailability.Available;
        set
        {
            if (value)
            {
                KeyAvailable = KeyAvailability.Available;
            }
        }
    }

    public bool IsKeyNotAvailable
    {
        get => KeyAvailable == KeyAvailability.NotAvailable;
        set
        {
            if (value)
            {
                KeyAvailable = KeyAvailability.NotAvailable;
            }
        }
    }

    public bool IsKeyUnknown
    {
        get => KeyAvailable == KeyAvailability.Unknown;
        set
        {
            if (value)
            {
                KeyAvailable = KeyAvailability.Unknown;
            }
        }
    }

    public DwellingStatus OriginalStatus { get; }

    /// <summary>One input per gas of the Einsatz, in list order (#410).</summary>
    public IReadOnlyList<GasInputRow> GasInputs { get; }

    public string? OriginalResidentName { get; }

    public KeyAvailability OriginalKeyAvailable { get; }

    public string? OriginalKeyNumber { get; }

    public string OriginalLabel { get; }

    /// <summary>
    /// The Wohnung's committed Messreihe, newest first. Read-only on purpose: a keystroke in the
    /// value field is not a measurement, and ABBRECHEN has to be able to discard it — a previewed
    /// row would put a reading in the list that never happened, with a timestamp that does not
    /// exist yet (it is minted by the clock inside RecordCoValue when FERTIG commits).
    /// </summary>
    public IReadOnlyList<CoReadingRow> Readings { get; }

    public bool HasReadings => Readings.Count > 0;

    public bool HasStatusChange => Status != OriginalStatus;

    public bool HasCoValueChange => GasInputs.Any(g => g.HasChange);

    /// <summary>What FERTIG records: every gas whose value changed, null where it was cleared.</summary>
    public IReadOnlyDictionary<Guid, decimal?> ChangedValues =>
        GasInputs.Where(g => g.HasChange).ToDictionary(g => g.Gas.Id, g => g.Committed);

    /// <summary>The values as they stand in the sidebar, for the tile's pending preview.</summary>
    public IReadOnlyDictionary<Guid, decimal> PendingValues =>
        GasInputs.Where(g => g.Committed is not null).ToDictionary(g => g.Gas.Id, g => g.Committed.GetValueOrDefault());

    // Normalized the same way Dwelling.WithDetails does, so clearing an already-empty name is not
    // mistaken for an edit and does not cost a save (or, on a joined device, a command POST).
    // The Schlüsselnummer is compared as it would be stored: one typed under "Keiner" is not kept.
    public bool HasDetailChange =>
        Normalize(ResidentName) != Normalize(OriginalResidentName)
        || KeyAvailable != OriginalKeyAvailable
        || StoredKeyNumber(KeyAvailable, KeyNumber) != StoredKeyNumber(OriginalKeyAvailable, OriginalKeyNumber);

    public bool HasLabelChange => Normalize(Label) != Normalize(OriginalLabel);

    /// <summary>Whether ABBRECHEN would throw anything away, so it has to ask first (#652).</summary>
    public bool HasChanges => HasStatusChange || HasCoValueChange || HasDetailChange || HasLabelChange;

    /// <summary>Whether any gas's value looks like a typo; each input carries its own hint.</summary>
    public bool IsCoImplausible => GasInputs.Any(g => g.IsImplausible);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? StoredKeyNumber(KeyAvailability keyAvailable, string? keyNumber) =>
        keyAvailable == KeyAvailability.Available ? Normalize(keyNumber) : null;

    // A typed value raises the editor's own change, so the tile preview (ApplyPendingEditToMatrix)
    // follows the sidebar as it did for the single CO value.
    private void OnGasInputChanged()
    {
        OnPropertyChanged(nameof(HasCoValueChange));
        OnPropertyChanged(nameof(IsCoImplausible));
    }
}

/// <summary>One Wohnung in the removal picker, with what it is carrying spelled out (#419) — the
/// crew decides which units go by reading this, so "leer" has to be visibly different from a unit
/// holding a Messwert.</summary>
public sealed partial class ApartmentRemovalRowViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public ApartmentRemovalRowViewModel(
        int apartmentNumber, string label, string contents, bool carriesData, bool isSelected, Action onSelectionChanged)
    {
        ApartmentNumber = apartmentNumber;
        Label = label;
        Contents = contents;
        CarriesData = carriesData;
        _isSelected = isSelected;
        _onSelectionChanged = onSelectionChanged;
    }

    public int ApartmentNumber { get; }

    public string Label { get; }

    /// <summary>"120 ppm · Bewohner erfasst", or "leer" for a Wohnung nobody has touched.</summary>
    public string Contents { get; }

    /// <summary>Drives the warning colour: ticking an empty Wohnung costs nothing, ticking this
    /// one throws away recorded work.</summary>
    public bool CarriesData { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        _ = value;
        _onSelectionChanged();
    }
}

/// <summary>The pending "which Wohnungen go?" question (#419), raised when shrinking a floor would
/// destroy something a crew recorded. Nothing here has reached the session: until ENTFERNEN is
/// pressed the floor is exactly as it was, which is what makes ABBRECHEN free.</summary>
public sealed partial class ApartmentRemovalViewModel : ObservableObject
{
    public ApartmentRemovalViewModel(
        Guid buildingId,
        int floorOrdinal,
        string floorLabel,
        int committedCount,
        int targetCount,
        IReadOnlyList<ApartmentRemovalRowViewModel> rows)
    {
        BuildingId = buildingId;
        FloorOrdinal = floorOrdinal;
        FloorLabel = floorLabel;
        CommittedCount = committedCount;
        TargetCount = targetCount;
        Rows = rows;
    }

    public Guid BuildingId { get; }

    public int FloorOrdinal { get; }

    public string FloorLabel { get; }

    /// <summary>What the floor holds right now — the count ABBRECHEN returns the spinner to.</summary>
    public int CommittedCount { get; }

    /// <summary>What the crew typed. The picker resolves *which* units go, never how many.</summary>
    public int TargetCount { get; }

    public IReadOnlyList<ApartmentRemovalRowViewModel> Rows { get; }

    public int RequiredCount => CommittedCount - TargetCount;

    public int SelectedCount => Rows.Count(r => r.IsSelected);

    public string Header => $"{FloorLabel} · {CommittedCount} → {TargetCount} WOHNUNGEN";

    public string SelectionLabel => $"{SelectedCount} von {RequiredCount} ausgewählt";

    /// <summary>Exactly as many as the typed count implies: confirming with fewer would leave the
    /// floor at a number nobody asked for.</summary>
    public bool CanConfirm => SelectedCount == RequiredCount;

    public IReadOnlyList<int> SelectedApartmentNumbers =>
        Rows.Where(r => r.IsSelected).Select(r => r.ApartmentNumber).ToList();

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionLabel));
        OnPropertyChanged(nameof(CanConfirm));
    }
}

/// <summary>The open "remove this whole Geschoss?" question (#443). Unlike
/// <see cref="ApartmentRemovalViewModel"/> there is nothing to pick -- the floor goes or it
/// doesn't -- so it only says what hangs on it.</summary>
public sealed class FloorRemovalViewModel
{
    public FloorRemovalViewModel(Guid buildingId, int floorOrdinal, int apartmentCount, IReadOnlyList<string> items)
    {
        BuildingId = buildingId;
        FloorOrdinal = floorOrdinal;
        ApartmentCount = apartmentCount;
        Items = items;
    }

    public Guid BuildingId { get; }

    public int FloorOrdinal { get; }

    public int ApartmentCount { get; }

    /// <summary>One line per Wohnung that carries something, e.g. "Mitte · 120 ppm".</summary>
    public IReadOnlyList<string> Items { get; }

    public string FloorLabel => CoMeasurementLabels.FloorLabel(FloorOrdinal);

    public string Header => $"{FloorLabel} ENTFERNEN";

    public string Question =>
        $"{FloorLabel} entfernen? {ApartmentCount} Wohnungen, davon {Items.Count} mit erfassten Daten.";
}

public sealed partial class FloorRowViewModel : ObservableObject
{
    private readonly Action<int, int> _onApartmentCountChanged;

    public FloorRowViewModel(
        int ordinal,
        string label,
        IReadOnlyList<DwellingCellViewModel> cells,
        string? description,
        int apartmentCount,
        bool isReadOnly,
        Action<int, int> onApartmentCountChanged,
        int searchedCount = 0,
        int affectedCount = 0)
    {
        Ordinal = ordinal;
        Label = label;
        Cells = cells;
        Description = description;
        IsReadOnly = isReadOnly;
        SearchedCount = searchedCount;
        AffectedCount = affectedCount;
        _apartmentCount = apartmentCount;
        _onApartmentCountChanged = onApartmentCountChanged;
    }

    public int Ordinal { get; }

    public string Label { get; }

    public IReadOnlyList<DwellingCellViewModel> Cells { get; }

    public string? Description { get; }

    public bool IsReadOnly { get; }

    /// <summary>Counted across the floor's whole population, not its visible Cells, so the band
    /// header keeps telling the truth while a status filter is narrowing what's on screen.</summary>
    public int SearchedCount { get; }

    public int AffectedCount { get; }

    public int ProcessedCount => SearchedCount + AffectedCount;

    /// <summary>"9/14" -- the one number a crew working a floor actually wants, and the reason the
    /// band header exists at all.</summary>
    public string ProgressLabel => $"{ProcessedCount}/{ApartmentCount}";

    public bool IsComplete => ApartmentCount > 0 && ProcessedCount >= ApartmentCount;

    public bool HasAffected => AffectedCount > 0;

    /// <summary>#265: this floor's own Wohnungen count, independent of every other floor's --
    /// edited directly on the row, growing/trimming its Cells.</summary>
    [ObservableProperty]
    private int _apartmentCount;

    partial void OnApartmentCountChanged(int value)
    {
        if (!IsReadOnly)
            _onApartmentCountChanged(Ordinal, value);
    }
}

public sealed partial class CoMessprotokollViewModel : ObservableObject, INarrowAware, IDisposable
{
    /// <summary>Pixel width of the summary progress track (see SearchedBarWidth).</summary>
    private const double ProgressTrackWidth = 420;

    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<Premises> _premises;

    private readonly IReadOnlyList<GasDefinition>? _gases;

    // premises: the Objekte a new Haus may be picked from (#420); none when omitted. gases: the
    // Stammdaten Messgase (#410), copied into the Einsatz by its first Haus; CO alone when omitted.
    public CoMessprotokollViewModel(
        IIncidentSession session, IClock clock, Action onChanged, IReadOnlyList<Premises>? premises = null, IReadOnlyList<GasDefinition>? gases = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _premises = premises ?? Array.Empty<Premises>();
        _gases = gases;
        PremisesOptions = _premises.Select(p => p.Name).ToList();
        IsReadOnly = session.IsReadOnly;
        _session.Changed += Refresh;
        Refresh();
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    public bool HasBuildings => BuildingOptions.Count > 0;

    public bool CanModify => !IsReadOnly && HasBuildings;

    public ObservableCollection<Building> BuildingOptions { get; } = new();

    [ObservableProperty]
    private Building? _selectedBuilding;

    [ObservableProperty]
    private ObservableCollection<FloorRowViewModel> _matrixRows = new();

    /// <summary>The open sidebar's edit buffer, or null when no unit is being edited. Nothing it
    /// holds has reached the session yet (#242).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen))]
    [NotifyPropertyChangedFor(nameof(ShowMatrix))]
    private DwellingEditorViewModel? _editor;

    public bool IsEditorOpen => Editor is not null;

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMatrix))]
    private bool _isNarrow;

    /// <summary>
    /// Whether the building matrix is on screen. The Wohnung editor is a 320px pane docked beside
    /// it, which at 412dp leaves ~91px for a matrix whose floor gutter alone used to be 104 — so
    /// on a phone the editor takes the screen while it is open and the matrix comes back when it
    /// closes. On a wide window both are visible at once, as before.
    /// </summary>
    public bool ShowMatrix => HasBuildings && !(IsNarrow && IsEditorOpen);

    /// <summary>The open "which Wohnungen go?" question, or null when none is pending (#419).
    /// Like <see cref="Editor"/>, nothing it holds has reached the session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApartmentRemovalOpen))]
    [NotifyCanExecuteChangedFor(nameof(CancelApartmentRemovalCommand))]
    private ApartmentRemovalViewModel? _pendingApartmentRemoval;

    public bool IsApartmentRemovalOpen => PendingApartmentRemoval is not null;

    /// <summary>The open "remove this Geschoss?" question, or null when none is pending (#443).
    /// Nothing it holds has reached the session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFloorRemovalOpen))]
    private FloorRemovalViewModel? _pendingFloorRemoval;

    public bool IsFloorRemovalOpen => PendingFloorRemoval is not null;

    /// <summary>Structure editing (each floor's Wohnungen count) is a setup job done once when the
    /// building is first described; measuring is what the view is for the rest of the incident.
    /// Keeping the count spinners permanently in the grid puts an edit control between the crew and
    /// every tile they need to read, so they live behind this toggle instead.</summary>
    [ObservableProperty]
    private bool _isStructureMode;

    /// <summary>Under pressure the question is "what's left", not "what exists". Filtering to the
    /// open or affected units collapses a 29-unit building to the handful that still need work.</summary>
    [ObservableProperty]
    private CoUnitFilter _filter = CoUnitFilter.All;

    public bool IsFilterAll => Filter == CoUnitFilter.All;

    public bool IsFilterOpen => Filter == CoUnitFilter.Open;

    public bool IsFilterAffected => Filter == CoUnitFilter.Affected;

    public int TotalUnits { get; private set; }

    public int SearchedUnits { get; private set; }

    public int AffectedUnits { get; private set; }

    public int OpenUnits => TotalUnits - SearchedUnits - AffectedUnits;

    /// <summary>The one-line answer to "how far are we", which no amount of scanning tiles gives
    /// you quickly on a 29-unit building.</summary>
    public string SummaryLine =>
        $"{TotalUnits} Einheiten · {SearchedUnits} durchsucht · {AffectedUnits} betroffen · {OpenUnits} offen";

    public string SelectedBuildingName => SelectedBuilding?.Name ?? string.Empty;

    // Segmented progress bar widths. Computed as pixels against a fixed track rather than star-sized
    // grid columns so the bar needs no value converter and stays readable at a glance.
    public double SearchedBarWidth => BarWidth(SearchedUnits);

    public double AffectedBarWidth => BarWidth(AffectedUnits);

    public double OpenBarWidth => ProgressTrackWidth - SearchedBarWidth - AffectedBarWidth;

    private double BarWidth(int count) =>
        TotalUnits <= 0 ? 0 : Math.Round(ProgressTrackWidth * count / (double)TotalUnits, 1);

    [RelayCommand]
    private void ShowAll() => Filter = CoUnitFilter.All;

    [RelayCommand]
    private void ShowOpen() => Filter = CoUnitFilter.Open;

    [RelayCommand]
    private void ShowAffected() => Filter = CoUnitFilter.Affected;

    partial void OnFilterChanged(CoUnitFilter value)
    {
        OnPropertyChanged(nameof(IsFilterAll));
        OnPropertyChanged(nameof(IsFilterOpen));
        OnPropertyChanged(nameof(IsFilterAffected));
        BuildMatrix();
    }

    partial void OnIsStructureModeChanged(bool value)
    {
        // Editing counts against a filtered view would show a floor "3/8" while three tiles are on
        // screen; structure work needs the whole floor visible.
        if (value)
        {
            Filter = CoUnitFilter.All;
        }
        else
        {
            // The spinner that raised the question is gone with the structure toolbar (#419), and
            // so is the ENTFERNEN button that raised a floor removal (#443).
            PendingApartmentRemoval = null;
            PendingFloorRemoval = null;
        }

        BuildMatrix();
    }

    private void Refresh()
    {
        // Any committed change — this device's or one pushed from a joined device — can move the
        // floor the pending question was asked about, and a picker listing Wohnungen that have
        // since shifted is worse than no picker at all. Drop it and let the crew re-type (#419).
        PendingApartmentRemoval = null;

        // Same for a floor removal: after any change the floor it names may no longer be the top
        // or bottom one, or may hold something the question didn't mention (#443).
        PendingFloorRemoval = null;

        // Capture the selection BEFORE clearing BuildingOptions: the "HAUS" ComboBox is two-way
        // bound to SelectedBuilding (SelectedItem="{Binding SelectedBuilding}") with BuildingOptions
        // as its ItemsSource. Clearing an ObservableCollection fires a Reset, and Avalonia's Selector
        // reacts to that by synchronously nulling its own SelectedItem — which pushes straight back
        // through the two-way binding and sets SelectedBuilding to null right here, before this
        // method ever reads it. Reading SelectedBuilding after the Clear/Add cycle (as before) always
        // saw that null and fell back to BuildingOptions.FirstOrDefault(), silently switching the
        // selected Haus to the first one in the list on every unrelated edit (e.g. entering a ppm
        // value). Matching by Id rather than object identity/equality is still correct and needed on
        // top of this — a remote session's Building instances aren't reference- or value-equal across
        // a snapshot round trip either — but doesn't help if the read itself already happened too late.
        var selectedId = SelectedBuilding?.Id;

        // #241: the null the ComboBox pushes back, and the re-selection after it, are this method
        // keeping the list current, not the crew switching Haus. Letting them through to
        // OnSelectedBuildingChanged discarded the open sidebar (a half-typed ppm value) on every
        // unrelated edit, and rebuilt the matrix twice more besides.
        _syncingBuildingOptions = true;
        try
        {
            SyncBuildingOptions();
            SelectedBuilding = selectedId is { } id
                ? BuildingOptions.FirstOrDefault(b => b.Id == id) ?? BuildingOptions.FirstOrDefault()
                : BuildingOptions.FirstOrDefault();
        }
        finally
        {
            _syncingBuildingOptions = false;
        }

        if (SelectedBuilding?.Id != selectedId)
        {
            // The selected Haus is really gone (or this is the first load): that is a switch.
            DiscardHausBoundState();
        }
        else if (Editor is { } editor && !_session.Incident.Dwellings.Any(d =>
            d.BuildingId == editor.BuildingId
            && d.FloorOrdinal == editor.FloorOrdinal
            && d.ApartmentNumber == editor.ApartmentNumber))
        {
            // A joined device removed the floor under the open sidebar; FERTIG would write nowhere.
            Editor = null;
        }

        BuildMatrix();
        NotifyHausCommandsChanged();
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(HasBuildings));
        OnPropertyChanged(nameof(ShowMatrix));
        OnPropertyChanged(nameof(CanModify));
    }

    private bool _syncingBuildingOptions;

    /// <summary>Brings BuildingOptions in line with the session without a Reset when the Haus list
    /// itself is unchanged (#241). Compared by Id: a joined session's Building instances are neither
    /// reference- nor value-equal across a snapshot round trip. A Haus whose structure changed
    /// (same Id, new record) is swapped in place, so the matrix still sees the fresh floor counts.</summary>
    private void SyncBuildingOptions()
    {
        var current = _session.Incident.Buildings;
        if (current.Select(b => b.Id).SequenceEqual(BuildingOptions.Select(b => b.Id)))
        {
            for (var i = 0; i < current.Count; i++)
            {
                if (!ReferenceEquals(BuildingOptions[i], current[i]))
                {
                    BuildingOptions[i] = current[i];
                }
            }

            return;
        }

        BuildingOptions.Clear();
        foreach (var b in current)
        {
            BuildingOptions.Add(b);
        }
    }

    partial void OnSelectedBuildingChanged(Building? value)
    {
        if (_syncingBuildingOptions)
        {
            return;
        }

        DiscardHausBoundState();
        BuildMatrix();
        NotifyHausCommandsChanged();
    }

    private void DiscardHausBoundState()
    {
        // A pending edit belongs to a unit in the Haus being left; carrying it across would leave
        // the sidebar editing a tile that is no longer on screen. Switching discards, like ABBRECHEN.
        Editor = null;

        // Same for a pending removal, which names a floor of the Haus being left (#419).
        PendingApartmentRemoval = null;
        PendingFloorRemoval = null;
    }

    private void NotifyHausCommandsChanged()
    {
        OnPropertyChanged(nameof(CanRemoveBuilding));
        AddUntergeschossCommand.NotifyCanExecuteChanged();
        AddObergeschossCommand.NotifyCanExecuteChanged();
        RemoveUntergeschossCommand.NotifyCanExecuteChanged();
        RemoveObergeschossCommand.NotifyCanExecuteChanged();
    }

    private void BuildMatrix()
    {
        MatrixRows.Clear();
        TotalUnits = 0;
        SearchedUnits = 0;
        AffectedUnits = 0;

        if (SelectedBuilding is null)
        {
            NotifySummaryChanged();
            return;
        }

        var building = SelectedBuilding;

        // One pass over the incident's Dwellings instead of a scan per tile. TryAdd keeps the first
        // match on a duplicate key, as the FirstOrDefault it replaces did.
        var dwellings = new Dictionary<(int Floor, int Apartment), Dwelling>();
        foreach (var d in _session.Incident.Dwellings.Where(x => x.BuildingId == building.Id))
        {
            dwellings.TryAdd((d.FloorOrdinal, d.ApartmentNumber), d);
        }

        for (var floor = building.FloorCount; floor >= -building.UndergroundFloorCount; floor--)
        {
            var apartmentCount = building.ApartmentsFor(floor);
            var all = Enumerable.Range(1, apartmentCount)
                .Select(apt =>
                {
                    var dwelling = dwellings.GetValueOrDefault((floor, apt));
                    return dwelling is not null
                        ? new DwellingCellViewModel(dwelling, building, IsReadOnly, OnOpenEditor, _session.Incident.Gases)
                        : null;
                })
                .Where(c => c is not null)
                .Cast<DwellingCellViewModel>()
                .ToList();

            // Tally over the floor's whole population before filtering, so both the band header and
            // the building summary keep counting what exists rather than what's currently on screen.
            var searched = all.Count(c => c.Status == DwellingStatus.Searched);
            var affected = all.Count(c => c.Status == DwellingStatus.Affected);
            TotalUnits += all.Count;
            SearchedUnits += searched;
            AffectedUnits += affected;

            var cells = Filter switch
            {
                CoUnitFilter.Open => all.Where(c => c.Status == DwellingStatus.NotSearched).ToList(),
                CoUnitFilter.Affected => all.Where(c => c.Status == DwellingStatus.Affected).ToList(),
                _ => all,
            };

            // A filtered-out floor is dropped entirely rather than left as an empty band: the point
            // of the filter is to shrink the view down to what still needs work.
            if (cells.Count == 0 && Filter != CoUnitFilter.All)
            {
                continue;
            }

            var description = building.FloorDescriptions.TryGetValue(floor, out var d) ? d : null;
            MatrixRows.Add(new FloorRowViewModel(
                floor,
                CoMeasurementLabels.FloorLabel(floor),
                cells,
                description,
                apartmentCount,
                IsReadOnly,
                OnApartmentCountChanged,
                searched,
                affected));
        }

        // After the tallies and the filter, both of which deliberately keep counting committed
        // state: an uncommitted intent should not move the "how far are we" figures, nor make the
        // unit you are editing vanish out from under you when a filter is on.
        ApplyPendingEditToMatrix();
        NotifySummaryChanged();
    }

    partial void OnEditorChanged(DwellingEditorViewModel? oldValue, DwellingEditorViewModel? newValue)
    {
        // Whatever replaced or closed the buffer (FERTIG, another tile, a removed floor) answered
        // the question about the old one.
        IsDiscardEditorConfirmOpen = false;

        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnEditorFieldChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnEditorFieldChanged;
        }
    }

    private void OnEditorFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
        ApplyPendingEditToMatrix();

    /// <summary>Mirrors the open sidebar's uncommitted values onto its tile, so the door mark
    /// updates as the crew works even though nothing has been written yet (#242). Re-applied after
    /// every rebuild: BuildMatrix discards and recreates every tile VM on each session change, so
    /// without this an unrelated (or remote) edit landing mid-edit would wipe the preview.</summary>
    private void ApplyPendingEditToMatrix()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        var cell = MatrixRows
            .SelectMany(r => r.Cells)
            .FirstOrDefault(c => c.BuildingId == editor.BuildingId
                && c.FloorOrdinal == editor.FloorOrdinal
                && c.ApartmentNumber == editor.ApartmentNumber);
        if (cell is null)
        {
            return;
        }

        cell.Status = editor.Status;
        cell.Values = editor.PendingValues;
        cell.ResidentName = editor.ResidentName;
        cell.KeyAvailable = editor.KeyAvailable;
        cell.KeyNumber = editor.IsKeyNumberEnabled ? editor.KeyNumber : null;
        cell.Label = editor.Label;
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(TotalUnits));
        OnPropertyChanged(nameof(SearchedUnits));
        OnPropertyChanged(nameof(AffectedUnits));
        OnPropertyChanged(nameof(OpenUnits));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(SelectedBuildingName));
        OnPropertyChanged(nameof(SearchedBarWidth));
        OnPropertyChanged(nameof(AffectedBarWidth));
        OnPropertyChanged(nameof(OpenBarWidth));
    }

    /// <summary>The floor spinner changed. Growing, or shrinking past Wohnungen nobody has touched,
    /// commits straight through as before; a shrink that would destroy recorded work stops here and
    /// asks which units go (#419).</summary>
    private void OnApartmentCountChanged(int floorOrdinal, int count)
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        var building = SelectedBuilding;
        var committed = building.ApartmentsFor(floorOrdinal);

        // Typed back to where it started -- the crew's own way out of a half-made change.
        if (count == committed)
        {
            PendingApartmentRemoval = null;
            return;
        }

        var onFloor = _session.Incident.Dwellings
            .Where(d => d.BuildingId == building.Id && d.FloorOrdinal == floorOrdinal)
            .OrderBy(d => d.ApartmentNumber)
            .ToList();

        // A Wohnung is worth asking about if it carries anything a crew put there -- including a
        // Bezeichnung, which lives on the Building rather than the Dwelling. Checking only
        // Dwelling.HasData would let a floor of named shops ("Kiosk", "Bäckerei") be trimmed away
        // without a word, which is the very failure this exists to stop.
        bool CarriesAnything(Dwelling d) =>
            d.HasData || building.HasApartmentLabel(floorOrdinal, d.ApartmentNumber);

        var doomedByTruncation = onFloor.Skip(count).ToList();
        if (count > committed || !doomedByTruncation.Exists(CarriesAnything))
        {
            PendingApartmentRemoval = null;
            _session.SetApartmentCount(building.Id, floorOrdinal, count);
            _onChanged();
            Refresh();
            return;
        }

        // The spinner fires per keystroke, so "14" -> "1" -> "12" lands here three times. Rebuild
        // the question for the newest number, but leave an identical one alone so a repeat
        // notification doesn't wipe the ticks the crew has already made.
        if (PendingApartmentRemoval is { } pending
            && pending.FloorOrdinal == floorOrdinal
            && pending.TargetCount == count
            && pending.BuildingId == building.Id)
        {
            return;
        }

        // One question at a time: two open panels would leave the crew unsure which ENTFERNEN is which.
        PendingFloorRemoval = null;
        PendingApartmentRemoval = BuildApartmentRemoval(building, floorOrdinal, count, committed, onFloor, CarriesAnything);
    }

    /// <summary>Seeds the picker: empty Wohnungen from the right first, then the rightmost units
    /// that do carry something, so the default selection is the least destructive one that still
    /// reaches the count the crew typed.</summary>
    private ApartmentRemovalViewModel BuildApartmentRemoval(
        Building building,
        int floorOrdinal,
        int count,
        int committed,
        IReadOnlyList<Dwelling> onFloor,
        Func<Dwelling, bool> carriesAnything)
    {
        var required = committed - count;
        var preselected = onFloor
            .Where(d => !carriesAnything(d))
            .OrderByDescending(d => d.ApartmentNumber)
            .Concat(onFloor.Where(carriesAnything).OrderByDescending(d => d.ApartmentNumber))
            .Take(required)
            .Select(d => d.ApartmentNumber)
            .ToHashSet();

        ApartmentRemovalViewModel? built = null;
        var rows = onFloor
            .Select(d => new ApartmentRemovalRowViewModel(
                d.ApartmentNumber,
                CoMeasurementLabels.ApartmentLabel(building, floorOrdinal, d.ApartmentNumber),
                DescribeContents(d),
                carriesAnything(d),
                preselected.Contains(d.ApartmentNumber),
                () => built?.NotifySelectionChanged()))
            .ToList();

        built = new ApartmentRemovalViewModel(
            building.Id,
            floorOrdinal,
            CoMeasurementLabels.FloorLabel(floorOrdinal),
            committed,
            count,
            rows);
        return built;
    }

    private string DescribeContents(Dwelling dwelling)
    {
        var parts = new List<string>();
        var gases = _session.Incident.Gases;
        var measured = CoMeasurementLabels.ValuesInOrder(gases, dwelling.Values);
        if (measured.Count > 0)
        {
            parts.AddRange(measured.Select(m => CoMeasurementLabels.GasValueLabel(m.Gas, m.Value, gases.Count > 1)));
        }
        else if (dwelling.Readings.Count > 0)
        {
            parts.Add("Messreihe");
        }

        if (dwelling.ResidentName is { } resident)
        {
            parts.Add(resident);
        }

        if (dwelling.Status == DwellingStatus.Searched)
        {
            parts.Add("durchsucht");
        }
        else if (dwelling.Status == DwellingStatus.Affected)
        {
            parts.Add("betroffen");
        }

        if (dwelling.KeyNumber is { } keyNumber)
        {
            parts.Add($"Schlüssel Nr. {keyNumber}");
        }
        else if (dwelling.KeyAvailable == KeyAvailability.Available)
        {
            parts.Add("Schlüssel vorhanden");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : "leer";
    }

    [RelayCommand]
    private void ConfirmApartmentRemoval()
    {
        if (PendingApartmentRemoval is not { } pending || !pending.CanConfirm)
        {
            return;
        }

        // Everything needed is read off into locals first: the session call below raises Changed,
        // which runs Refresh and tears down MatrixRows and this very view model underneath us.
        var buildingId = pending.BuildingId;
        var floorOrdinal = pending.FloorOrdinal;
        var selected = pending.SelectedApartmentNumbers;

        // The open sidebar addresses its unit by position, so after the renumber below FERTIG would
        // write to a different Wohnung than the one on screen. Discard it, as switching Haus does.
        Editor = null;
        PendingApartmentRemoval = null;

        _session.RemoveDwellings(buildingId, floorOrdinal, selected);
        _onChanged();
        Refresh();
    }

    // Gated so the Esc bound on the matrix (#538) does nothing, and in particular does not rebuild
    // the rows under the focused spinner, while no removal is pending.
    [RelayCommand(CanExecute = nameof(IsApartmentRemovalOpen))]
    private void CancelApartmentRemoval()
    {
        PendingApartmentRemoval = null;

        // Rebuilds every FloorRowViewModel from committed state, which seeds the spinner's backing
        // field directly -- so the typed number snaps back without re-entering this whole path.
        Refresh();
    }

    private void OnOpenEditor(Guid buildingId, int floorOrdinal, int apartmentNumber)
    {
        // Seeded from the domain rather than from the tile VM: the tile may still be showing a
        // previous pending preview, and the buffer's Original* values must be the committed truth
        // for the dirty checks in ConfirmEditor to mean anything.
        var building = _session.Incident.Buildings.FirstOrDefault(b => b.Id == buildingId);
        var dwelling = _session.Incident.Dwellings.FirstOrDefault(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == floorOrdinal && d.ApartmentNumber == apartmentNumber);

        Editor = building is not null && dwelling is not null
            ? new DwellingEditorViewModel(
                dwelling, building, _session.Incident.InvolvedPartiesIn(dwelling.Id).Select(p => p.Name).ToList(), _session.Incident.Gases)
            : null;
    }

    [RelayCommand(CanExecute = nameof(CanAddBuilding))]
    private void AddBuilding() => IsAddBuildingDialogOpen = true;

    private bool CanAddBuilding => !IsReadOnly;

    // Quiet until the first press: a dialog that opens already scolding teaches nothing.
    private bool _errorsShown;

    [ObservableProperty]
    private bool _isAddBuildingDialogOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewBuildingNameError))]
    [NotifyPropertyChangedFor(nameof(NewBuildingPremisesHint))]
    private string _newBuildingName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewBuildingPremisesHint))]
    private int _newBuildingFloors = Premises.DefaultFloorCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewBuildingPremisesHint))]
    private int _newBuildingApartments = Premises.DefaultApartmentsPerFloor;

    /// <summary>Untergeschosse aren't visible in the matrix until scrolled to, unlike ground/upper
    /// floors, so most Häuser have at least one at creation time (#218); UG HINZUFÜGEN still covers
    /// adding more, or a building with none, after the fact.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewBuildingPremisesHint))]
    private int _newBuildingUndergroundFloors = Premises.DefaultUndergroundFloorCount;

    // The Objekt the Bezeichnung names (#420), and what its pick put into each count. A count that
    // still holds its default or that value is the pick's to change; any other was typed.
    private Premises? _pickedPremises;
    private int? _filledFloors;
    private int? _filledApartments;
    private int? _filledUndergroundFloors;

    /// <summary>The names of the Objekte a Haus can be picked from (#420).</summary>
    public IReadOnlyList<string> PremisesOptions { get; }

    /// <summary>Whether the Haus still has no Bezeichnung, once the operator has asked (#412).</summary>
    public string? NewBuildingNameError =>
        _errorsShown && string.IsNullOrWhiteSpace(NewBuildingName) ? ValidationMessages.Required : null;

    /// <summary>What a picked Objekt brings, e.g. "Aus den Stammdaten · Lindenallee 12, 12345
    /// Musterstadt · 4 Wohnungsbezeichnungen", or null when the Bezeichnung names none. The count is of
    /// the labels that fit the structure as it now stands; the others are not created.</summary>
    public string? NewBuildingPremisesHint
    {
        get
        {
            if (_pickedPremises is not { } premises)
            {
                return null;
            }

            var labels = CoMeasurementLabels.LabelsWithin(
                premises.ApartmentLabels, NewBuildingFloors, NewBuildingApartments, NewBuildingUndergroundFloors).Count;
            var parts = new List<string> { "Aus den Stammdaten" };
            if (premises.AddressLine.Length > 0)
            {
                parts.Add(premises.AddressLine);
            }

            if (labels > 0)
            {
                parts.Add(labels == 1 ? "1 Wohnungsbezeichnung" : $"{labels} Wohnungsbezeichnungen");
            }

            return string.Join(" · ", parts);
        }
    }

    // Like a Straße in the Einsatzdaten (#461): only a name that matches fills anything, so typing
    // on past an Objekt's name keeps the counts it brought but no longer its labels.
    partial void OnNewBuildingNameChanged(string value)
    {
        _pickedPremises = StammdatenCatalogue.Find(value, _premises);
        if (_pickedPremises is not { } premises)
        {
            return;
        }

        NewBuildingFloors = Fill(NewBuildingFloors, ref _filledFloors, Premises.DefaultFloorCount, premises.FloorCount);
        NewBuildingApartments = Fill(NewBuildingApartments, ref _filledApartments, Premises.DefaultApartmentsPerFloor, premises.ApartmentsPerFloor);
        NewBuildingUndergroundFloors = Fill(
            NewBuildingUndergroundFloors, ref _filledUndergroundFloors, Premises.DefaultUndergroundFloorCount, premises.UndergroundFloorCount);
    }

    private static int Fill(int current, ref int? filled, int initial, int value)
    {
        if (current != initial && current != filled)
        {
            return current;
        }

        filled = value;
        return value;
    }

    [RelayCommand]
    private void ConfirmAddBuilding()
    {
        ShowErrors(true);
        if (NewBuildingNameError is not null)
        {
            return;
        }

        // The labels are the Objekt's own copy (#337); Building.Create keeps only those that fit.
        // The Messgase are copied the same way, by the first Haus only (#410).
        _session.AddCoBuilding(
            NewBuildingName, NewBuildingFloors, NewBuildingApartments, NewBuildingUndergroundFloors, _pickedPremises?.ApartmentLabels, _gases);
        NewBuildingName = string.Empty;
        _filledFloors = _filledApartments = _filledUndergroundFloors = null;
        NewBuildingFloors = Premises.DefaultFloorCount;
        NewBuildingApartments = Premises.DefaultApartmentsPerFloor;
        NewBuildingUndergroundFloors = Premises.DefaultUndergroundFloorCount;
        IsAddBuildingDialogOpen = false;
        ShowErrors(false); // the cleared dialog must not reopen already complaining
        _onChanged();
        Refresh();
    }

    [RelayCommand]
    private void CancelAddBuilding()
    {
        IsAddBuildingDialogOpen = false;
        ShowErrors(false);
    }

    private void ShowErrors(bool shown)
    {
        _errorsShown = shown;
        OnPropertyChanged(nameof(NewBuildingNameError));
    }

    [RelayCommand(CanExecute = nameof(CanRemoveBuilding))]
    private void RemoveBuilding()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        IsRemoveBuildingConfirmOpen = true;
    }

    private bool CanRemoveBuilding => !IsReadOnly && SelectedBuilding is not null;

    [ObservableProperty]
    private bool _isRemoveBuildingConfirmOpen;

    [RelayCommand]
    private void ConfirmRemoveBuilding()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.RemoveCoBuilding(SelectedBuilding.Id);
        IsRemoveBuildingConfirmOpen = false;
        _onChanged();
        Refresh();
    }

    [RelayCommand]
    private void CancelRemoveBuilding() => IsRemoveBuildingConfirmOpen = false;

    /// <summary>Adds one Untergeschoss below the current lowest floor (#218), up to the 3-floor
    /// cap. Reuses UpdateCoBuildingStructure, so it also creates the new floor's Wohnungen.</summary>
    [RelayCommand(CanExecute = nameof(CanAddUntergeschoss))]
    private void AddUntergeschoss()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.UpdateCoBuildingStructure(
            SelectedBuilding.Id,
            SelectedBuilding.FloorCount,
            SelectedBuilding.ApartmentsPerFloor,
            SelectedBuilding.UndergroundFloorCount + 1);
        _onChanged();
        Refresh();
    }

    private bool CanAddUntergeschoss =>
        !IsReadOnly && SelectedBuilding is not null && SelectedBuilding.UndergroundFloorCount < 3;

    /// <summary>Adds one Obergeschoss above the current highest floor (#258), mirroring
    /// AddUntergeschoss: reuses UpdateCoBuildingStructure, so it also creates the new floor's
    /// Wohnungen, capped at Building's 50-floor limit.</summary>
    [RelayCommand(CanExecute = nameof(CanAddObergeschoss))]
    private void AddObergeschoss()
    {
        if (SelectedBuilding is null)
        {
            return;
        }

        _session.UpdateCoBuildingStructure(
            SelectedBuilding.Id,
            SelectedBuilding.FloorCount + 1,
            SelectedBuilding.ApartmentsPerFloor,
            SelectedBuilding.UndergroundFloorCount);
        _onChanged();
        Refresh();
    }

    private bool CanAddObergeschoss =>
        !IsReadOnly && SelectedBuilding is not null && SelectedBuilding.FloorCount < 50;

    /// <summary>Removes the highest Obergeschoss (#443), the way back from OG HINZUFÜGEN. EG plus
    /// one OG is Building's floor, so the button stops there.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveObergeschoss))]
    private void RemoveObergeschoss()
    {
        if (SelectedBuilding is { } building)
        {
            RequestFloorRemoval(building, building.FloorCount);
        }
    }

    private bool CanRemoveObergeschoss => !IsReadOnly && SelectedBuilding is { FloorCount: > 1 };

    /// <summary>Removes the lowest Untergeschoss (#443), down to none at all.</summary>
    [RelayCommand(CanExecute = nameof(CanRemoveUntergeschoss))]
    private void RemoveUntergeschoss()
    {
        if (SelectedBuilding is { } building)
        {
            RequestFloorRemoval(building, -building.UndergroundFloorCount);
        }
    }

    private bool CanRemoveUntergeschoss => !IsReadOnly && SelectedBuilding is { UndergroundFloorCount: > 0 };

    /// <summary>An empty floor goes straight away -- trimming a freshly described building is the
    /// normal case and must not nag, the same rule as the Wohnungen spinner (#419). A floor
    /// carrying anything a crew put there stops and asks, naming what hangs on it.</summary>
    private void RequestFloorRemoval(Building building, int floorOrdinal)
    {
        var onFloor = _session.Incident.Dwellings
            .Where(d => d.BuildingId == building.Id && d.FloorOrdinal == floorOrdinal)
            .OrderBy(d => d.ApartmentNumber)
            .ToList();

        // Same notion of "carries" as the spinner's question: a Bezeichnung lives on the Building.
        var items = onFloor
            .Where(d => d.HasData || building.HasApartmentLabel(floorOrdinal, d.ApartmentNumber))
            .Select(d => $"{CoMeasurementLabels.ApartmentLabel(building, floorOrdinal, d.ApartmentNumber)} · {DescribeContents(d)}")
            .ToList();

        if (items.Count == 0)
        {
            PendingFloorRemoval = null;
            CommitFloorRemoval(building, floorOrdinal);
            return;
        }

        PendingApartmentRemoval = null;
        PendingFloorRemoval = new FloorRemovalViewModel(building.Id, floorOrdinal, onFloor.Count, items);
    }

    [RelayCommand]
    private void ConfirmFloorRemoval()
    {
        if (PendingFloorRemoval is not { } pending)
        {
            return;
        }

        PendingFloorRemoval = null;
        if (SelectedBuilding is { } building && building.Id == pending.BuildingId)
        {
            CommitFloorRemoval(building, pending.FloorOrdinal);
        }
    }

    [RelayCommand]
    private void CancelFloorRemoval() => PendingFloorRemoval = null;

    /// <summary>Shrinks the structure by exactly the named floor, and only while it still is the
    /// top OG or bottom UG: the counts sent are absolute, so acting on a stale ordinal would take a
    /// different Geschoss than the one the crew was shown.</summary>
    private void CommitFloorRemoval(Building building, int floorOrdinal)
    {
        var floorCount = building.FloorCount;
        var undergroundFloorCount = building.UndergroundFloorCount;
        if (floorOrdinal > 0 && floorOrdinal == floorCount)
        {
            floorCount--;
        }
        else if (floorOrdinal < 0 && floorOrdinal == -undergroundFloorCount)
        {
            undergroundFloorCount--;
        }
        else
        {
            return;
        }

        // The open sidebar may address a unit on the floor about to go -- empty floors included,
        // since opening a tile doesn't make it carry anything -- and FERTIG would then write to a
        // Wohnung that no longer exists. Discard it, as switching Haus does.
        Editor = null;
        _session.UpdateCoBuildingStructure(building.Id, floorCount, building.ApartmentsPerFloor, undergroundFloorCount);
        _onChanged();
        Refresh();
    }

    /// <summary>ABBRECHEN, and Esc. Drops the buffer without writing anything; the rebuild puts the
    /// tile back to committed state, undoing the pending preview. A buffer holding a change asks
    /// first (#652): a typed ppm value that never reaches FERTIG is a gap in the Messreihe, and
    /// every removal in this view already asks. An untouched one closes at once, so the keyboard
    /// flow costs no extra key.</summary>
    [RelayCommand]
    private void CloseEditor()
    {
        if (Editor is { HasChanges: true })
        {
            IsDiscardEditorConfirmOpen = true;
            return;
        }

        DiscardEditor();
    }

    [ObservableProperty]
    private bool _isDiscardEditorConfirmOpen;

    [RelayCommand]
    private void ConfirmDiscardEditor() => DiscardEditor();

    [RelayCommand]
    private void CancelDiscardEditor() => IsDiscardEditorConfirmOpen = false;

    private void DiscardEditor()
    {
        Editor = null;
        BuildMatrix();
    }

    [RelayCommand]
    private void SetEditorStatusNotSearched() => SetEditorStatus(DwellingStatus.NotSearched);

    [RelayCommand]
    private void SetEditorStatusSearched() => SetEditorStatus(DwellingStatus.Searched);

    [RelayCommand]
    private void SetEditorStatusAffected() => SetEditorStatus(DwellingStatus.Affected);

    private void SetEditorStatus(DwellingStatus status)
    {
        if (Editor is null)
        {
            return;
        }

        Editor.Status = status;
    }

    /// <summary>FERTIG. The single commit point for a unit: writes only the fields that actually
    /// changed, so the Einsatztagebuch gets one entry for the status and one for the gas readings
    /// rather than one per keystroke, and a joined device does not POST no-op commands.</summary>
    [RelayCommand]
    private void ConfirmEditor()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        // Clear the buffer before writing: each session write raises Changed -> Refresh ->
        // BuildMatrix, and leaving it set would re-apply the pending preview on top of the values
        // just committed.
        Editor = null;
        BuildMatrix();

        if (IsReadOnly)
        {
            return;
        }

        var wrote = false;
        if (editor.HasStatusChange)
        {
            _session.SetDwellingStatus(editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.Status);
            wrote = true;
        }

        if (editor.HasCoValueChange)
        {
            _session.RecordCoValues(editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.ChangedValues);
            wrote = true;
        }

        if (editor.HasDetailChange)
        {
            _session.SetDwellingDetails(
                editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.ResidentName, editor.KeyAvailable, editor.KeyNumber);
            wrote = true;
        }

        if (editor.HasLabelChange)
        {
            _session.SetApartmentLabel(editor.BuildingId, editor.FloorOrdinal, editor.ApartmentNumber, editor.Label);
            wrote = true;
        }

        if (wrote)
        {
            _onChanged();
        }
    }
}
