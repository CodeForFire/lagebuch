using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The MESSPROTOKOLL tab (#930): a general ABC/Gefahrstoff Messprotokoll, not bound to a building.
/// Four parts behind one segmented control: the Messreihe with its tiles and entry form, the
/// Messpunkte with the weather log, the Messgrößen, and the Geräte.
/// </summary>
/// <remarks>
/// Every change goes through the session and comes back through <see cref="IIncidentSession.Changed"/>,
/// so a Messung recorded on a joined device appears here as one recorded on this one. What is
/// typed is read here, never in the view: a value "12,5", a range "8–14", a time "14:05". A
/// closed Einsatz is a historical record, so every command is inert instead of throwing.
/// </remarks>
public sealed partial class MeasurementLogViewModel : ObservableObject, INarrowAware, IDisposable
{
    /// <summary>Who an assessment is by unless the Lagebuchführer says otherwise.</summary>
    public const string DefaultAssessor = "Fachberater ABC";

    /// <summary>Where a wind reading comes from unless the Lagebuchführer says otherwise.</summary>
    public const string DefaultWeatherSource = "Handwindmesser";

    /// <summary>The message under a Messgröße input that is not a reading.</summary>
    public const string ValueFormatError = "Zahl oder Bereich eingeben, z. B. 12,5 oder 8–14";

    private readonly IIncidentSession _session;
    private readonly IClock _clock;
    private readonly Action _onChanged;
    private readonly IReadOnlyList<GasDefinition>? _gases;
    private readonly Action<string, string, Action> _requestConfirm;

    // Set while Refresh moves the filter, which must not overwrite the form's Messpunkt.
    private bool _refreshingFilter;

    /// <param name="session">The incident session every change goes through.</param>
    /// <param name="clock">What "now" is for the time boxes' defaults.</param>
    /// <param name="onChanged">Called after each accepted change, as every module does.</param>
    /// <param name="gases">
    /// The Stammdaten Messgase, copied into the Einsatz as its Messgrößen by the first Messpunkt or
    /// Messgröße, the way the first Haus copies them for the Gasmessprotokoll; CO alone when omitted
    /// or empty. Afterwards they are only offered to add.
    /// </param>
    /// <param name="requestConfirm">
    /// Asks the host to confirm a removal before running it, as <see cref="IssuedItemsViewModel"/>
    /// does. Defaults to running it immediately.
    /// </param>
    public MeasurementLogViewModel(
        IIncidentSession session,
        IClock clock,
        Action onChanged,
        IReadOnlyList<GasDefinition>? gases = null,
        Action<string, string, Action>? requestConfirm = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(onChanged);
        _session = session;
        _clock = clock;
        _onChanged = onChanged;
        _gases = gases;
        _requestConfirm = requestConfirm ?? ((_, _, onConfirmed) => onConfirmed());
        IsReadOnly = session.IsReadOnly;
        _newTime = MeasurementInput.TimeText(clock.Now);
        _weatherTime = _newTime;
        _selectedPointFilter = AllPoints;
        Refresh();
        _session.Changed += Refresh;
    }

    public void Dispose() => _session.Changed -= Refresh;

    public bool IsReadOnly { get; }

    /// <summary>Whether anything can be recorded: false once the Einsatz is closed.</summary>
    public bool CanEdit => !IsReadOnly;

    /// <inheritdoc />
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isNarrow;

    /// <summary>Whether the entry form is open, on any width.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private bool _isComposerOpen;

    /// <summary>
    /// Whether the entry form is on screen: only while composing, and never while a Bewertung or a
    /// Korrektur takes its place. Unlike the one-line forms elsewhere it is closed by default on a
    /// wide window too: open for good, its four rows left the Messreihe no room on a laptop.
    /// </summary>
    public bool ShowComposer => CanEdit && !IsAssessmentOpen && !IsCorrectionOpen && IsComposerOpen;

    /// <summary>The "+ MESSUNG ERFASSEN" button, shown exactly when the form could open and is not.</summary>
    public bool ShowComposerButton => CanEdit && !IsComposerOpen && !IsAssessmentOpen && !IsCorrectionOpen;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void OpenComposer() => IsComposerOpen = true;

    [RelayCommand]
    private void CloseComposer()
    {
        IsComposerOpen = false;
        EntryError = null;
    }

    /// <summary>Why the last change from a list row (AKTIV, ENTFERNEN) was refused, or null.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>The part on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMessreihePage))]
    [NotifyPropertyChangedFor(nameof(IsMesspunkteWetterPage))]
    [NotifyPropertyChangedFor(nameof(IsMessgroessenPage))]
    [NotifyPropertyChangedFor(nameof(IsGeraetePage))]
    private MeasurementLogPage _selectedPage = MeasurementLogPage.Messreihe;

    public bool IsMessreihePage => SelectedPage == MeasurementLogPage.Messreihe;

    public bool IsMesspunkteWetterPage => SelectedPage == MeasurementLogPage.MesspunkteWetter;

    public bool IsMessgroessenPage => SelectedPage == MeasurementLogPage.Messgroessen;

    public bool IsGeraetePage => SelectedPage == MeasurementLogPage.Geraete;

    [RelayCommand]
    private void SelectPage(MeasurementLogPage page)
    {
        if (Enum.IsDefined(page))
        {
            SelectedPage = page;
        }
    }

    public static IReadOnlyList<ZoneOption> ZoneOptions { get; } =
        Enum.GetValues<MeasurementZone>().Select(z => new ZoneOption(z, MeasurementLabels.Zone(z))).ToList();

    public static IReadOnlyList<ProtectionOption> ProtectionOptions { get; } =
        Enum.GetValues<ProtectiveEquipment>().Select(p => new ProtectionOption(p, MeasurementLabels.Protection(p))).ToList();

    public static IReadOnlyList<ModeOption> ModeOptions { get; } =
        Enum.GetValues<MeasurementMode>().Select(m => new ModeOption(m, MeasurementLabels.Mode(m))).ToList();

    public static IReadOnlyList<StatusOption> StatusOptions { get; } =
        Enum.GetValues<MeasurementStatus>().Select(s => new StatusOption(s, MeasurementLabels.Status(s))).ToList();

    public static IReadOnlyList<WindOption> WindOptions { get; } =
        Enum.GetValues<WindDirection>().Select(w => new WindOption(w, MeasurementLabels.Wind(w))).ToList();

    public static IReadOnlyList<CloudOption> CloudOptions { get; } =
        Enum.GetValues<CloudCover>().Select(c => new CloudOption(c, MeasurementLabels.Clouds(c))).ToList();

    public static IReadOnlyList<PrecipitationOption> PrecipitationOptions { get; } =
        Enum.GetValues<Precipitation>().Select(p => new PrecipitationOption(p, MeasurementLabels.Precipitation(p))).ToList();

    public static IReadOnlyList<DeviceKindOption> DeviceKindOptions { get; } =
        Enum.GetValues<DeviceKind>().Select(k => new DeviceKindOption(k, MeasurementLabels.DeviceKind(k))).ToList();

    public static IReadOnlyList<GasDirectionOption> DirectionOptions => GasesSection.DirectionChoices;

    public static IReadOnlyList<string> UnitSuggestions => GasDefinition.UnitSuggestions;

    public static IReadOnlyList<string> WeatherSourceSuggestions { get; } =
        [DefaultWeatherSource, "Wetterstation Fahrzeug", "DWD", "Schätzung"];

    // The caps the view hands to each box's MaxLength, so typing stops where the domain would cut.
    public static int MaxPointNameLength => MeasuringPoint.MaxNameLength;

    public static int MaxLocationLength => MeasuringPoint.MaxLocationLength;

    public static int MaxOrderLength => Measurement.MaxOrderLength;

    public static int MaxTeamLength => Measurement.MaxTeamLength;

    public static int MaxNoteLength => Measurement.MaxNoteLength;

    public static int MaxAssessorLength => MeasurementAssessment.MaxByLength;

    public static int MaxAssessmentTextLength => MeasurementAssessment.MaxTextLength;

    public static int MaxReasonLength => MeasurementCorrection.MaxReasonLength;

    public static int MaxSourceLength => WeatherObservation.MaxSourceLength;

    public static int MaxChannelNameLength => GasDefinition.MaxNameLength;

    public static int MaxChannelUnitLength => GasDefinition.MaxUnitLength;

    public static int MaxChannelNoteLength => MeasurementChannel.MaxNoteLength;

    public static int MaxDeviceNameLength => MeasuringDevice.MaxNameLength;

    public static int MaxDeviceDetailLength => MeasuringDevice.MaxNumberLength;

    private static PointFilterOption AllPoints { get; } = new(null, "Alle");

    /// <summary>One tile per active Messgröße, with its latest value that is not an Ausreißer.</summary>
    public ObservableCollection<MeasurementTile> Tiles { get; } = new();

    /// <summary>The Messgrößen as columns of the Messreihe, inactive ones included, in list order.</summary>
    public IReadOnlyList<MeasurementColumn> Columns { get; private set; } = [];

    /// <summary>"Alle", then each Messpunkt.</summary>
    public IReadOnlyList<PointFilterOption> PointFilterOptions { get; private set; } = [];

    /// <summary>Which Messpunkt the tiles and the list show; "Alle" by default.</summary>
    [ObservableProperty]
    private PointFilterOption? _selectedPointFilter;

    partial void OnSelectedPointFilterChanged(PointFilterOption? value)
    {
        BuildMessreihe(_session.Incident.MeasurementLog);

        // A crew working one Messpunkt reports from it: the form follows the filter.
        if (!_refreshingFilter && value?.PointId is not null)
        {
            NewPointName = value.Label;
        }
    }

    /// <summary>The Messungen at the filtered Messpunkt, newest first.</summary>
    public ObservableCollection<MeasurementRow> Rows { get; } = new();

    public bool HasRows => Rows.Count > 0;

    /// <summary>What the list says while it is empty, or null while it is not.</summary>
    public string? EmptyText => HasRows
        ? null
        : SelectedPointFilter?.PointId is null ? "Noch keine Messung." : "Noch keine Messung an diesem Messpunkt.";

    /// <summary>The Uhrzeit, "14:05"; now by default.</summary>
    [ObservableProperty]
    private string _newTime;

    /// <summary>The Messauftrag, "MA 05".</summary>
    [ObservableProperty]
    private string? _newOrder;

    /// <summary>The Messpunkt, picked from <see cref="PointNameOptions"/> or typed; a name the
    /// Einsatz does not know yet is created with the Messung.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPoint))]
    private string? _newPointName;

    /// <summary>The Trupp, "Funkrufname · Trupp".</summary>
    [ObservableProperty]
    private string? _newTeam;

    [ObservableProperty]
    private ProtectiveEquipment _newProtection = ProtectiveEquipment.ScbaForm1;

    [ObservableProperty]
    private MeasurementMode _newMode = MeasurementMode.Remote;

    [ObservableProperty]
    private bool _newOdour;

    [ObservableProperty]
    private bool _newDeposits;

    [ObservableProperty]
    private string? _newNote;

    /// <summary>Why the Messung was not recorded, or null.</summary>
    [ObservableProperty]
    private string? _entryError;

    /// <summary>One input per active Messgröße. While the Einsatz has none yet, one per Stammdaten
    /// Messgas the first Messpunkt will copy in.</summary>
    public ObservableCollection<MeasurementValueInput> ValueInputs { get; } = new();

    /// <summary>Shown in place of the inputs while nothing can be measured.</summary>
    public string? NoChannelsHint => ValueInputs.Count == 0 ? "Keine aktive Messgröße – unter MESSGRÖSSEN anlegen." : null;

    /// <summary>The Messpunkte by name, for the form's suggestions.</summary>
    public IReadOnlyList<string> PointNameOptions { get; private set; } = [];

    /// <summary>Every Messauftrag recorded so far, newest first.</summary>
    public IReadOnlyList<string> OrderOptions { get; private set; } = [];

    /// <summary>Every Trupp recorded so far, newest first.</summary>
    public IReadOnlyList<string> TeamOptions { get; private set; } = [];

    /// <summary>Whether the typed Messpunkt is new and will be created with the Messung.</summary>
    public bool IsNewPoint => !string.IsNullOrWhiteSpace(NewPointName) && FindPoint(NewPointName) is null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void RecordMeasurement()
    {
        if (IsReadOnly)
        {
            return;
        }

        EntryError = null;
        foreach (var input in ValueInputs)
        {
            input.Error = null;
        }

        if (!MeasurementInput.TryParseTime(NewTime, _clock.Now, out var measuredAt))
        {
            EntryError = "Uhrzeit als SS:MM eingeben, z. B. 14:05";
            return;
        }

        var pointName = NewPointName?.Trim();
        if (string.IsNullOrEmpty(pointName))
        {
            EntryError = "Messpunkt eingeben";
            return;
        }

        var parsed = new List<(Guid? ChannelId, string Name, MeasuredValue Value)>();
        foreach (var input in ValueInputs)
        {
            if (!MeasurementInput.TryParseValue(input.Text, out var value))
            {
                input.Error = ValueFormatError;
                EntryError ??= $"{input.Name}: {ValueFormatError}";
                continue;
            }

            if (value is not null)
            {
                parsed.Add((input.ChannelId, input.Name, value));
            }
        }

        if (EntryError is not null)
        {
            return;
        }

        if (parsed.Count == 0 && !NewOdour && !NewDeposits && string.IsNullOrWhiteSpace(NewNote))
        {
            EntryError = "Messwert, Geruch, Niederschlag oder Bemerkung eintragen";
            return;
        }

        var point = FindPoint(pointName);
        if (point is null)
        {
            EntryError = Run(() => _session.AddMeasuringPoint(pointName, null, MeasurementZone.Unknown, _gases));
            point = FindPoint(pointName);
            if (EntryError is not null || point is null)
            {
                return;
            }
        }

        // An input that stood for a Stammdaten Messgas finds the Messgröße the new Messpunkt made of it.
        var channels = _session.Incident.MeasurementChannels;
        var values = new Dictionary<Guid, MeasuredValue>();
        foreach (var (channelId, name, value) in parsed)
        {
            var id = channelId ?? channels.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id;
            if (id is { } known)
            {
                values[known] = value;
            }
        }

        var pointId = point.Id;
        EntryError = Run(() => _session.RecordMeasurement(
            measuredAt, NewOrder, pointId, NewTeam, NewProtection, NewMode, NewOdour, NewDeposits, NewNote, values));
        if (EntryError is not null)
        {
            return;
        }

        // The Messpunkt, the Trupp, its PSA and the Messauftrag stay: the next report is usually
        // from the same crew. What was measured goes.
        foreach (var input in ValueInputs)
        {
            input.Text = null;
        }

        NewOdour = false;
        NewDeposits = false;
        NewNote = null;
        NewTime = MeasurementInput.TimeText(_clock.Now);
        IsComposerOpen = false;
    }

    /// <summary>The Messung being assessed, or null while the dialog is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssessmentOpen))]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    private MeasurementRow? _assessmentTarget;

    public bool IsAssessmentOpen => AssessmentTarget is not null;

    [ObservableProperty]
    private MeasurementStatus _assessmentStatus = MeasurementStatus.Confirmed;

    /// <summary>Who assesses; required.</summary>
    [ObservableProperty]
    private string? _assessmentBy;

    /// <summary>Bewertung / abgeleitete Maßnahme.</summary>
    [ObservableProperty]
    private string? _assessmentText;

    [ObservableProperty]
    private string? _assessmentError;

    private void OpenAssessment(MeasurementRow row)
    {
        AssessmentStatus = row.Status == MeasurementStatus.Unconfirmed ? MeasurementStatus.Confirmed : row.Status;
        AssessmentBy = row.AssessmentBy ?? DefaultAssessor;
        AssessmentText = row.AssessmentText;
        AssessmentError = null;
        AssessmentTarget = row;
    }

    [RelayCommand]
    private void ConfirmAssessment()
    {
        if (IsReadOnly || AssessmentTarget is not { } row)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(AssessmentBy))
        {
            AssessmentError = "Bewertet durch: Name eingeben";
            return;
        }

        var by = AssessmentBy;
        AssessmentError = Run(() => _session.AssessMeasurement(row.Id, AssessmentStatus, by, AssessmentText));
        if (AssessmentError is null)
        {
            AssessmentTarget = null;
        }
    }

    [RelayCommand]
    private void CancelAssessment()
    {
        AssessmentTarget = null;
        AssessmentError = null;
    }

    /// <summary>The value being corrected, or null while the dialog is closed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCorrectionOpen))]
    [NotifyPropertyChangedFor(nameof(ShowComposer))]
    [NotifyPropertyChangedFor(nameof(ShowComposerButton))]
    [NotifyPropertyChangedFor(nameof(CorrectionHeading))]
    private MeasurementCell? _correctionTarget;

    public bool IsCorrectionOpen => CorrectionTarget is not null;

    /// <summary>"CO (ppm) · bisher: 140".</summary>
    public string? CorrectionHeading => CorrectionTarget is { } cell
        ? $"{(cell.ChannelUnit.Length == 0 ? cell.ChannelName : $"{cell.ChannelName} ({cell.ChannelUnit})")} · bisher: {cell.Text}"
        : null;

    /// <summary>The new value; empty takes the value out of the Messung.</summary>
    [ObservableProperty]
    private string? _correctionValue;

    /// <summary>Why the value changes; required.</summary>
    [ObservableProperty]
    private string? _correctionReason;

    [ObservableProperty]
    private string? _correctionError;

    private void OpenCorrection(MeasurementCell cell)
    {
        CorrectionValue = cell.Value is { } value ? MeasurementLabels.Number(value) : null;
        CorrectionReason = null;
        CorrectionError = null;
        CorrectionTarget = cell;
    }

    [RelayCommand]
    private void ConfirmCorrection()
    {
        if (IsReadOnly || CorrectionTarget is not { } cell)
        {
            return;
        }

        if (!MeasurementInput.TryParseValue(CorrectionValue, out var value))
        {
            CorrectionError = ValueFormatError;
            return;
        }

        if (value == cell.Value)
        {
            CorrectionError = "Der Messwert ist unverändert.";
            return;
        }

        if (string.IsNullOrWhiteSpace(CorrectionReason))
        {
            CorrectionError = "Grund eingeben, z. B. Ablesefehler";
            return;
        }

        var reason = CorrectionReason;
        CorrectionError = Run(() => _session.CorrectMeasurementValue(cell.MeasurementId, cell.ChannelId, value, reason));
        if (CorrectionError is null)
        {
            CorrectionTarget = null;
        }
    }

    [RelayCommand]
    private void CancelCorrection()
    {
        CorrectionTarget = null;
        CorrectionError = null;
    }

    public ObservableCollection<MeasuringPointRow> Points { get; } = new();

    [ObservableProperty]
    private string? _pointName;

    [ObservableProperty]
    private string? _pointLocation;

    [ObservableProperty]
    private MeasurementZone _pointZone = MeasurementZone.Danger;

    /// <summary>The Messpunkt the form corrects, or null while it creates one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingPoint))]
    [NotifyPropertyChangedFor(nameof(PointSubmitLabel))]
    private Guid? _editingPointId;

    public bool IsEditingPoint => EditingPointId is not null;

    public string PointSubmitLabel => IsEditingPoint ? "SPEICHERN" : "MESSPUNKT ANLEGEN";

    [ObservableProperty]
    private string? _pointError;

    private void EditPoint(MeasuringPointRow row)
    {
        PointName = row.Name;
        PointLocation = row.Location;
        PointZone = row.Zone;
        PointError = null;
        EditingPointId = row.Id;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SavePoint()
    {
        if (IsReadOnly)
        {
            return;
        }

        var name = PointName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            PointError = "Messpunkt: Namen eingeben";
            return;
        }

        if (FindPoint(name) is { } same && same.Id != EditingPointId)
        {
            PointError = $"Messpunkt {same.Name} gibt es schon.";
            return;
        }

        var location = PointLocation;
        var zone = PointZone;
        PointError = EditingPointId is { } id
            ? Run(() => _session.UpdateMeasuringPoint(id, name, location, zone))
            : Run(() => _session.AddMeasuringPoint(name, location, zone, _gases));
        if (PointError is null)
        {
            ResetPointForm();
        }
    }

    [RelayCommand]
    private void CancelPointEdit() => ResetPointForm();

    private void ResetPointForm()
    {
        PointName = null;
        PointLocation = null;
        PointZone = MeasurementZone.Danger;
        PointError = null;
        EditingPointId = null;
    }

    /// <summary>The weather log, newest first.</summary>
    public ObservableCollection<WeatherRow> Weather { get; } = new();

    public bool HasWeather => Weather.Count > 0;

    [ObservableProperty]
    private string _weatherTime;

    [ObservableProperty]
    private WindDirection _weatherWind = WindDirection.W;

    /// <summary>km/h, optional.</summary>
    [ObservableProperty]
    private string? _weatherSpeed;

    /// <summary>°C, optional, may be negative.</summary>
    [ObservableProperty]
    private string? _weatherTemperature;

    [ObservableProperty]
    private CloudCover _weatherClouds = CloudCover.Clear;

    [ObservableProperty]
    private Precipitation _weatherPrecipitation = Precipitation.None;

    [ObservableProperty]
    private string? _weatherSource = DefaultWeatherSource;

    [ObservableProperty]
    private string? _weatherError;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddWeather()
    {
        if (IsReadOnly)
        {
            return;
        }

        if (!MeasurementInput.TryParseTime(WeatherTime, _clock.Now, out var observedAt))
        {
            WeatherError = "Uhrzeit als SS:MM eingeben, z. B. 14:05";
            return;
        }

        if (!MeasurementInput.TryParseOptionalNumber(WeatherSpeed, out var speed)
            || (speed is { } s && WeatherObservation.NormalizeWindSpeed(s) is null))
        {
            WeatherError = $"Windgeschwindigkeit: 0 bis {Formatting.Measurement(WeatherObservation.MaxWindSpeedKmh, "km/h")}";
            return;
        }

        if (!MeasurementInput.TryParseOptionalNumber(WeatherTemperature, out var temperature)
            || (temperature is { } t && WeatherObservation.NormalizeTemperature(t) is null))
        {
            WeatherError = "Temperatur: −60 bis 60 °C";
            return;
        }

        WeatherError = Run(() => _session.AddWeatherObservation(
            observedAt, WeatherWind, speed, temperature, WeatherClouds, WeatherPrecipitation, WeatherSource));
        if (WeatherError is null)
        {
            WeatherSpeed = null;
            WeatherTemperature = null;
            WeatherTime = MeasurementInput.TimeText(_clock.Now);
        }
    }

    public ObservableCollection<MeasurementChannelRow> Channels { get; } = new();

    /// <summary>The Stammdaten Messgase the Einsatz does not measure yet.</summary>
    public IReadOnlyList<GasChoice> GasChoices { get; private set; } = [];

    [ObservableProperty]
    private GasChoice? _selectedGasChoice;

    /// <summary>"ohne Gerät", then each Messgerät.</summary>
    public IReadOnlyList<DeviceChoice> DeviceChoices { get; private set; } = [];

    /// <summary>The Gerät a new Messgröße is read from; null or "ohne Gerät" for none.</summary>
    [ObservableProperty]
    private DeviceChoice? _newChannelDevice;

    [ObservableProperty]
    private string? _newChannelNote;

    [ObservableProperty]
    private string? _newChannelName;

    [ObservableProperty]
    private string? _newChannelUnit;

    [ObservableProperty]
    private GasDirection _newChannelDirection = GasDirection.Rising;

    [ObservableProperty]
    private decimal? _newChannelElevated;

    [ObservableProperty]
    private decimal? _newChannelDangerous;

    [ObservableProperty]
    private decimal? _newChannelLethal;

    [ObservableProperty]
    private decimal? _newChannelPlausibleUpTo;

    [ObservableProperty]
    private string? _channelError;

    /// <summary>What the first Messpunkt or Messgröße will copy in, while the Einsatz has no
    /// Messgrößen yet; null afterwards.</summary>
    public string? SeedHint { get; private set; }

    /// <summary>Whether <see cref="MeasurementChannel.MaxCount"/> Messgrößen are there.</summary>
    public bool IsChannelCapReached { get; private set; }

    /// <summary>Said once the list is full, beside the add buttons.</summary>
    public static string ChannelCapMessage { get; } =
        $"Höchstens {MeasurementChannel.MaxCount} Messgrößen – eine entfernen oder deaktivieren.";

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddGasChoice()
    {
        if (IsReadOnly)
        {
            return;
        }

        if (SelectedGasChoice is not { } choice)
        {
            ChannelError = "Messgas wählen";
            return;
        }

        AddChannel(choice.Definition);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void AddCustomChannel()
    {
        if (IsReadOnly)
        {
            return;
        }

        var definition = new GasDefinition(
            (NewChannelName ?? string.Empty).Trim(),
            (NewChannelUnit ?? string.Empty).Trim(),
            NewChannelDirection,
            NewChannelElevated,
            NewChannelDangerous,
            NewChannelLethal,
            NewChannelPlausibleUpTo);
        if (ValidateChannel(definition) is { } problem)
        {
            ChannelError = problem;
            return;
        }

        if (AddChannel(definition))
        {
            NewChannelName = null;
            NewChannelUnit = null;
            NewChannelDirection = GasDirection.Rising;
            NewChannelElevated = null;
            NewChannelDangerous = null;
            NewChannelLethal = null;
            NewChannelPlausibleUpTo = null;
        }
    }

    /// <summary>
    /// The first reason a free Messgröße would be refused, in German, or null when it is fine. The
    /// Stammdaten editor's rules (#410): a name, numbers a reading can be, thresholds in the gas's
    /// own direction.
    /// </summary>
    public static string? ValidateChannel(GasDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            return "Messgröße: Namen eingeben";
        }

        if (definition.Name.Trim().Length > GasDefinition.MaxNameLength)
        {
            return $"Messgröße: höchstens {GasDefinition.MaxNameLength} Zeichen";
        }

        if (definition.Unit.Trim().Length > GasDefinition.MaxUnitLength)
        {
            return $"Einheit: höchstens {GasDefinition.MaxUnitLength} Zeichen";
        }

        var numbers = new[] { definition.Elevated, definition.Dangerous, definition.Lethal, definition.PlausibleUpTo };
        if (numbers.Any(n => n is { } v && GasDefinition.NormalizeValue(v) is null))
        {
            return $"Schwellen: 0 bis {Formatting.Measurement(GasDefinition.MaxValue, string.Empty)}";
        }

        return definition.ThresholdsInOrder ? null : "Schwellen nicht in Reihenfolge";
    }

    private bool AddChannel(GasDefinition definition)
    {
        ChannelError = null;
        if (IsChannelCapReached)
        {
            ChannelError = ChannelCapMessage;
            return false;
        }

        var name = definition.Name.Trim();
        if (ChannelNamesAfterSeeding().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            ChannelError = $"Messgröße {name} gibt es schon.";
            return false;
        }

        var deviceId = NewChannelDevice?.DeviceId;
        var note = NewChannelNote;
        ChannelError = Run(() => _session.AddMeasurementChannel(definition, deviceId, note, _gases));
        if (ChannelError is not null)
        {
            return false;
        }

        SelectedGasChoice = null;
        NewChannelNote = null;
        return true;
    }

    private void SetChannelActive(MeasurementChannelRow row, bool isActive)
    {
        if (_session.Incident.MeasurementChannels.FirstOrDefault(c => c.Id == row.Id) is not { } channel)
        {
            return;
        }

        Error = Run(() => _session.UpdateMeasurementChannel(channel.Id, channel.DeviceId, isActive, channel.Note));
    }

    private void RemoveChannel(MeasurementChannelRow row) =>
        _requestConfirm(
            "Messgröße entfernen?",
            $"„{row.Name}“ aus dem Messprotokoll entfernen. Fortfahren?",
            () => Error = Run(() => _session.RemoveMeasurementChannel(row.Id)));

    public ObservableCollection<MeasuringDeviceRow> Devices { get; } = new();

    [ObservableProperty]
    private DeviceKind _newDeviceKind = DeviceKind.MultiGas;

    [ObservableProperty]
    private string? _newDeviceName;

    /// <summary>Geräte-Nr. / Charge.</summary>
    [ObservableProperty]
    private string? _newDeviceNumber;

    [ObservableProperty]
    private string? _newDeviceVehicle;

    /// <summary>Prüfung / Haltbarkeit.</summary>
    [ObservableProperty]
    private string? _newDeviceCheck;

    /// <summary>The Messgerät the form corrects, or null while it adds one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingDevice))]
    [NotifyPropertyChangedFor(nameof(DeviceSubmitLabel))]
    private Guid? _editingDeviceId;

    public bool IsEditingDevice => EditingDeviceId is not null;

    public string DeviceSubmitLabel => IsEditingDevice ? "SPEICHERN" : "GERÄT HINZUFÜGEN";

    [ObservableProperty]
    private string? _deviceError;

    private void EditDevice(MeasuringDeviceRow row)
    {
        NewDeviceKind = row.Kind;
        NewDeviceName = row.Name;
        NewDeviceNumber = row.Number;
        NewDeviceVehicle = row.Vehicle;
        NewDeviceCheck = row.Check;
        DeviceError = null;
        EditingDeviceId = row.Id;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SaveDevice()
    {
        if (IsReadOnly)
        {
            return;
        }

        var name = NewDeviceName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            DeviceError = "Gerät: Bezeichnung eingeben";
            return;
        }

        var (kind, number, vehicle, check) = (NewDeviceKind, NewDeviceNumber, NewDeviceVehicle, NewDeviceCheck);
        DeviceError = EditingDeviceId is { } id
            ? Run(() => _session.UpdateMeasuringDevice(id, kind, name, number, vehicle, check))
            : Run(() => _session.AddMeasuringDevice(kind, name, number, vehicle, check));
        if (DeviceError is null)
        {
            ResetDeviceForm();
        }
    }

    [RelayCommand]
    private void CancelDeviceEdit() => ResetDeviceForm();

    private void ResetDeviceForm()
    {
        NewDeviceKind = DeviceKind.MultiGas;
        NewDeviceName = null;
        NewDeviceNumber = null;
        NewDeviceVehicle = null;
        NewDeviceCheck = null;
        DeviceError = null;
        EditingDeviceId = null;
    }

    private void RemoveDevice(MeasuringDeviceRow row) =>
        _requestConfirm(
            "Messgerät entfernen?",
            $"„{row.Name}“ entfernen. Die Messgrößen bleiben, ohne Gerät. Fortfahren?",
            () =>
            {
                Error = Run(() => _session.RemoveMeasuringDevice(row.Id));
                if (EditingDeviceId == row.Id)
                {
                    ResetDeviceForm();
                }
            });

    // Runs a session command and tells the host. The Domain's refusals are German sentences about a
    // state a peer got to first, or a cap, so they are shown as they are; null means it went through.
    private string? Run(Action command)
    {
        try
        {
            command();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return DomainRefusal.Message(ex);
        }

        _onChanged();
        return null;
    }

    private MeasuringPoint? FindPoint(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : _session.Incident.MeasuringPoints.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    // What the Einsatz will measure once the next add has run: its own list, or the Stammdaten
    // list the first add copies in.
    private IReadOnlyList<GasDefinition> SeedPreview()
    {
        var copied = GasDefinition.SanitizeList(_gases);
        return copied.Count > 0 ? copied : [GasDefinition.DefaultCo];
    }

    private IEnumerable<string> ChannelNamesAfterSeeding()
    {
        var channels = _session.Incident.MeasurementChannels;
        return channels.Count > 0 ? channels.Select(c => c.Name) : SeedPreview().Select(g => g.Name);
    }

    // Rebuild from the incident on any change — this device's, or (when joined) another's.
    private void Refresh()
    {
        var log = _session.Incident.MeasurementLog;
        var devicesById = log.Devices.ToDictionary(d => d.Id);
        string? DeviceNameOf(Guid? id) => id is { } d && devicesById.TryGetValue(d, out var device) ? device.Name : null;

        // The filter keeps its Messpunkt while it exists; one removed elsewhere falls back to Alle.
        PointFilterOptions = [AllPoints, .. log.Points.Select(p => new PointFilterOption(p.Id, p.Name))];
        OnPropertyChanged(nameof(PointFilterOptions));
        var filterId = SelectedPointFilter?.PointId;
        var filter = PointFilterOptions.FirstOrDefault(o => o.PointId == filterId) ?? AllPoints;
        if (!ReferenceEquals(filter, SelectedPointFilter))
        {
            // A refresh moving the filter is not the Lagebuchführer picking a Messpunkt, so the
            // form's Messpunkt stays as typed.
            _refreshingFilter = true;
            try
            {
                SelectedPointFilter = filter;
            }
            finally
            {
                _refreshingFilter = false;
            }
        }

        Columns = log.Channels.Select(c => new MeasurementColumn(c.Id, c.Name, c.Unit, DeviceNameOf(c.DeviceId), c.IsActive)).ToList();
        OnPropertyChanged(nameof(Columns));
        BuildMessreihe(log);
        RebuildValueInputs(log);

        PointNameOptions = log.Points.Select(p => p.Name).ToList();
        OnPropertyChanged(nameof(PointNameOptions));
        var newestFirst = log.Measurements.Reverse().ToList();
        OrderOptions = newestFirst.Select(m => m.Order).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        OnPropertyChanged(nameof(OrderOptions));
        TeamOptions = newestFirst.Select(m => m.Team).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        OnPropertyChanged(nameof(TeamOptions));
        OnPropertyChanged(nameof(IsNewPoint));

        var counts = log.Measurements.GroupBy(m => m.PointId).ToDictionary(g => g.Key, g => g.Count());
        Points.Clear();
        foreach (var point in log.Points)
        {
            Points.Add(new MeasuringPointRow(point, counts.GetValueOrDefault(point.Id), IsReadOnly, EditPoint));
        }

        Weather.Clear();
        foreach (var observation in log.Weather.Select((w, i) => (w, i)).OrderByDescending(x => x.w.ObservedAt).ThenByDescending(x => x.i))
        {
            Weather.Add(new WeatherRow(observation.w));
        }

        OnPropertyChanged(nameof(HasWeather));

        var withReadings = log.Measurements
            .SelectMany(m => m.Values.Keys.Concat(m.Corrections.Select(c => c.ChannelId)))
            .ToHashSet();
        Channels.Clear();
        foreach (var channel in log.Channels)
        {
            Channels.Add(new MeasurementChannelRow(
                channel, DeviceNameOf(channel.DeviceId), withReadings.Contains(channel.Id), IsReadOnly, SetChannelActive, RemoveChannel));
        }

        var present = ChannelNamesAfterSeeding().ToHashSet(StringComparer.OrdinalIgnoreCase);
        GasChoices = GasDefinition.SanitizeList(_gases)
            .Where(g => !present.Contains(g.Name))
            .Select(g => new GasChoice(g, g.Unit.Length == 0 ? g.Name : $"{g.Name} ({g.Unit})"))
            .ToList();
        OnPropertyChanged(nameof(GasChoices));
        if (SelectedGasChoice is { } chosen && !GasChoices.Contains(chosen))
        {
            SelectedGasChoice = null;
        }

        SeedHint = log.Channels.Count == 0
            ? $"Mit dem ersten Messpunkt oder der ersten Messgröße kommen die Messgase aus den Stammdaten dazu: {string.Join(", ", SeedPreview().Select(g => g.Name))}."
            : null;
        OnPropertyChanged(nameof(SeedHint));
        IsChannelCapReached = log.Channels.Count >= MeasurementChannel.MaxCount;
        OnPropertyChanged(nameof(IsChannelCapReached));

        DeviceChoices = [new DeviceChoice(null, "ohne Gerät"), .. log.Devices.Select(d => new DeviceChoice(d.Id, $"{d.Name} · {MeasurementLabels.DeviceKind(d.Kind)}"))];
        OnPropertyChanged(nameof(DeviceChoices));
        NewChannelDevice = DeviceChoices.FirstOrDefault(c => c.DeviceId == NewChannelDevice?.DeviceId) ?? DeviceChoices[0];

        Devices.Clear();
        foreach (var device in log.Devices)
        {
            Devices.Add(new MeasuringDeviceRow(device, IsReadOnly, EditDevice, RemoveDevice));
        }
    }

    private void BuildMessreihe(MeasurementLog log)
    {
        var pointId = SelectedPointFilter?.PointId;
        var pointsById = log.Points.ToDictionary(p => p.Id);

        // Per Messgröße the worst of the Messpunkte' latest values, the newer on a tie: under "Alle"
        // a harmless later reading at one Messpunkt must not hide a dangerous one at another.
        var channelsById = log.Channels.ToDictionary(c => c.Id);
        var latest = MeasurementRules.Latest(log, pointId)
            .GroupBy(l => l.ChannelId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(l => MeasurementRules.SeverityOf(channelsById[l.ChannelId], l.Value)).ThenBy(l => l.MeasuredAt).Last());
        var devicesById = log.Devices.ToDictionary(d => d.Id);
        Tiles.Clear();
        foreach (var channel in log.Channels.Where(c => c.IsActive))
        {
            var device = channel.DeviceId is { } d && devicesById.TryGetValue(d, out var found) ? found.Name : null;
            if (latest.TryGetValue(channel.Id, out var value))
            {
                Tiles.Add(new MeasurementTile(channel, device, value.Value, pointsById[value.PointId].Name, value.MeasuredAt));
            }
            else
            {
                Tiles.Add(new MeasurementTile(channel, device, null, null, null));
            }
        }

        // The running number counts a Messpunkt's Messungen in time order, as MeasurementRules does.
        var numbers = new Dictionary<Guid, int>();
        foreach (var group in log.Measurements.GroupBy(m => m.PointId))
        {
            var n = 0;
            foreach (var measurement in group.OrderBy(m => m.MeasuredAt))
            {
                numbers[measurement.Id] = ++n;
            }
        }

        Rows.Clear();
        var shown = log.Measurements
            .Select((m, i) => (Measurement: m, Index: i))
            .Where(x => pointId is null || x.Measurement.PointId == pointId)
            .OrderByDescending(x => x.Measurement.MeasuredAt)
            .ThenByDescending(x => x.Index);
        foreach (var (measurement, _) in shown)
        {
            var cells = log.Channels
                .Select(c => new MeasurementCell(
                    measurement.Id,
                    c,
                    measurement.Values.TryGetValue(c.Id, out var v) ? v : null,
                    measurement.Corrections,
                    IsReadOnly,
                    OpenCorrection))
                .ToList();
            Rows.Add(new MeasurementRow(measurement, pointsById[measurement.PointId], numbers[measurement.Id], cells, IsReadOnly, OpenAssessment));
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(EmptyText));
    }

    // Replaced only when the active Messgrößen changed, and then carrying what was typed, so a
    // Messung recorded on another device never wipes a value half entered here.
    private void RebuildValueInputs(MeasurementLog log)
    {
        // A fresh log shows the Messgase the first Messpunkt will copy in; one whose Messgrößen were
        // all removed shows none, since nothing would copy them in again.
        var wanted = new List<(Guid? Id, GasDefinition Definition)>();
        if (log.Channels.Count > 0)
        {
            wanted.AddRange(log.Channels.Where(c => c.IsActive).Select<MeasurementChannel, (Guid?, GasDefinition)>(c => (c.Id, c.Definition)));
        }
        else if (log.Points.Count == 0)
        {
            wanted.AddRange(SeedPreview().Select<GasDefinition, (Guid?, GasDefinition)>(g => (null, g)));
        }

        if (wanted.Select(w => (w.Id, w.Definition.Name)).SequenceEqual(ValueInputs.Select(i => (i.ChannelId, i.Name))))
        {
            return;
        }

        var typed = ValueInputs
            .Where(i => !string.IsNullOrWhiteSpace(i.Text))
            .ToList();
        string? TextFor(Guid? id, string name) =>
            typed.FirstOrDefault(i => (id is not null && i.ChannelId == id) || string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))?.Text;

        ValueInputs.Clear();
        foreach (var (id, definition) in wanted)
        {
            ValueInputs.Add(new MeasurementValueInput(id, definition, TextFor(id, definition.Name)));
        }

        OnPropertyChanged(nameof(NoChannelsHint));
    }
}
