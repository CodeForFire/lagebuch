using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Measurements;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One Messgröße's tile above the Messreihe: its latest value that is not an Ausreißer, at the
/// Messpunkt the filter names or at any of them, coloured by its band.
/// </summary>
public sealed class MeasurementTile
{
    public MeasurementTile(MeasurementChannel channel, string? deviceName, MeasuredValue? value, string? pointName, DateTimeOffset? at)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ChannelId = channel.Id;
        Name = channel.Name;
        Unit = channel.Unit;
        DeviceName = deviceName;
        HasValue = value is not null;
        ValueText = value is null ? "–" : MeasurementLabels.Value(channel, value);
        Severity = value is null ? CoSeverity.Normal : MeasurementRules.SeverityOf(channel, value);
        SeverityText = MeasurementLabels.Severity(Severity);
        IsImplausible = value is not null && MeasurementRules.IsImplausible(channel, value);
        Footer = value is not null && at is { } time ? $"{pointName} · {Formatting.TimeOfDay(time)}" : "keine Messung";
    }

    public Guid ChannelId { get; }

    public string Name { get; }

    public string Unit { get; }

    public string? DeviceName { get; }

    public bool HasValue { get; }

    /// <summary>"38 ppm", "8–14 %UEG", or "–" while nothing is measured.</summary>
    public string ValueText { get; }

    public CoSeverity Severity { get; }

    /// <summary>"Erhöht", "Gefährlich", "Lebensgefährlich", or null: the colour never carries the
    /// band alone.</summary>
    public string? SeverityText { get; }

    public bool IsElevated => Severity == CoSeverity.Elevated;

    public bool IsDangerous => Severity == CoSeverity.Dangerous;

    public bool IsLethal => Severity == CoSeverity.Lethal;

    /// <summary>The value is beyond what the gas can plausibly read: a hint to check it.</summary>
    public bool IsImplausible { get; }

    /// <summary>"Kellerabgang · 14:05", or "keine Messung".</summary>
    public string Footer { get; }
}

/// <summary>One Messgröße's value in one row of the Messreihe. KORRIGIEREN opens the correction.</summary>
public sealed partial class MeasurementCell : ObservableObject
{
    private readonly Action<MeasurementCell> _onCorrect;

    public MeasurementCell(
        Guid measurementId,
        MeasurementChannel channel,
        MeasuredValue? value,
        IReadOnlyList<MeasurementCorrection> corrections,
        bool isReadOnly,
        Action<MeasurementCell> onCorrect)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(corrections);
        ArgumentNullException.ThrowIfNull(onCorrect);
        MeasurementId = measurementId;
        ChannelId = channel.Id;
        ChannelName = channel.Name;
        ChannelUnit = channel.Unit;
        Value = value;
        IsReadOnly = isReadOnly;
        _onCorrect = onCorrect;
        HasValue = value is not null;
        Text = value is null ? "–" : MeasurementLabels.Number(value);
        Severity = value is null ? CoSeverity.Normal : MeasurementRules.SeverityOf(channel, value);
        SeverityText = MeasurementLabels.Severity(Severity);
        IsImplausible = value is not null && MeasurementRules.IsImplausible(channel, value);
        var own = corrections.Where(c => c.ChannelId == channel.Id).ToList();
        IsCorrected = own.Count > 0;
        CorrectionText = IsCorrected
            ? "korrigiert: " + string.Join(
                " · ",
                own.Select(c => $"{(c.Previous is { } p ? MeasurementLabels.Number(p) : "–")} → {(c.Value is { } v ? MeasurementLabels.Number(v) : "–")} ({c.Reason}, {c.By}, {Formatting.TimeOfDay(c.At)})"))
            : null;
    }

    public Guid MeasurementId { get; }

    public Guid ChannelId { get; }

    public string ChannelName { get; }

    public string ChannelUnit { get; }

    /// <summary>The value as recorded, or null when this Messung has none for the Messgröße.</summary>
    public MeasuredValue? Value { get; }

    public bool IsReadOnly { get; }

    public bool HasValue { get; }

    /// <summary>"38", "8–14", or "–".</summary>
    public string Text { get; }

    public CoSeverity Severity { get; }

    /// <summary>"Erhöht", "Gefährlich", "Lebensgefährlich", or null: the cell's colour never carries
    /// the band alone.</summary>
    public string? SeverityText { get; }

    public bool IsElevated => Severity == CoSeverity.Elevated;

    public bool IsDangerous => Severity == CoSeverity.Dangerous;

    public bool IsLethal => Severity == CoSeverity.Lethal;

    public bool IsImplausible { get; }

    /// <summary>Whether the value was corrected after it was recorded; marked "KORR" in the row.</summary>
    public bool IsCorrected { get; }

    /// <summary>"korrigiert: 140 → 14 (Ablesefehler, Müller, 14:20)", or null.</summary>
    public string? CorrectionText { get; }

    [RelayCommand(CanExecute = nameof(CanCorrect))]
    private void Correct()
    {
        if (CanCorrect)
        {
            _onCorrect(this);
        }
    }

    private bool CanCorrect => !IsReadOnly;
}

/// <summary>One Messung in the Messreihe, newest first. BEWERTEN opens its assessment.</summary>
public sealed partial class MeasurementRow : ObservableObject
{
    private readonly Action<MeasurementRow> _onAssess;

    public MeasurementRow(
        Measurement measurement,
        MeasuringPoint point,
        int runningNumber,
        IReadOnlyList<MeasurementCell> cells,
        bool isReadOnly,
        Action<MeasurementRow> onAssess)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(onAssess);
        Id = measurement.Id;
        _onAssess = onAssess;
        IsReadOnly = isReadOnly;
        MeasuredAt = measurement.MeasuredAt;
        TimeText = Formatting.TimeOfDay(measurement.MeasuredAt);
        Order = measurement.Order;
        PointName = point.Name;
        PointText = $"{point.Name} #{runningNumber}";
        ZoneText = MeasurementLabels.Zone(point.Zone);
        Team = measurement.Team;
        ProtectionText = MeasurementLabels.Protection(measurement.Protection);
        ModeText = MeasurementLabels.Mode(measurement.Mode);
        Odour = measurement.Odour;
        Deposits = measurement.Deposits;
        ObservationText = string.Join(" · ", new[] { Odour ? "Geruch" : null, Deposits ? "Ruß/Niederschlag" : null }.OfType<string>());
        Note = measurement.Note;
        Cells = cells;
        Status = measurement.Status;
        StatusText = MeasurementLabels.Status(measurement.Status);
        if (measurement.Assessment is { } assessment)
        {
            AssessmentText = assessment.Text;
            AssessmentBy = assessment.By;
            AssessmentTimeText = Formatting.TimeOfDay(assessment.At);
        }

        Heading = string.Join(" · ", new[] { TimeText, Order ?? "ohne Auftrag", PointText, Team }.OfType<string>());
    }

    public Guid Id { get; }

    public bool IsReadOnly { get; }

    public DateTimeOffset MeasuredAt { get; }

    /// <summary>"14:05".</summary>
    public string TimeText { get; }

    /// <summary>The Messauftrag, "MA 04", or null.</summary>
    public string? Order { get; }

    public string PointName { get; }

    /// <summary>"Kellerabgang #3": the Messpunkt and how many Messungen it has had up to this one.</summary>
    public string PointText { get; }

    /// <summary>"Gefahrenbereich".</summary>
    public string ZoneText { get; }

    public string? Team { get; }

    public string ProtectionText { get; }

    public string ModeText { get; }

    public bool Odour { get; }

    public bool Deposits { get; }

    /// <summary>"Geruch · Ruß/Niederschlag", or empty.</summary>
    public string ObservationText { get; }

    public string? Note { get; }

    /// <summary>One cell per Messgröße, in the order of <see cref="MeasurementLogViewModel.Columns"/>.</summary>
    public IReadOnlyList<MeasurementCell> Cells { get; }

    public MeasurementStatus Status { get; }

    /// <summary>"unbestätigt", "bestätigt", "Ausreißer".</summary>
    public string StatusText { get; }

    public bool IsConfirmed => Status == MeasurementStatus.Confirmed;

    /// <summary>An Ausreißer stays in the list, struck through; it no longer counts for the tiles.</summary>
    public bool IsOutlier => Status == MeasurementStatus.Outlier;

    public string? AssessmentText { get; }

    public string? AssessmentBy { get; }

    public string? AssessmentTimeText { get; }

    public bool HasAssessment => AssessmentBy is not null;

    /// <summary>"14:05 · MA 04 · Kellerabgang #3 · Florian Musterstadt 40/1", above the dialogs.</summary>
    public string Heading { get; }

    [RelayCommand(CanExecute = nameof(CanAssess))]
    private void Assess()
    {
        if (CanAssess)
        {
            _onAssess(this);
        }
    }

    private bool CanAssess => !IsReadOnly;
}

/// <summary>One Messpunkt in the list beside the weather log. BEARBEITEN fills the form with it.</summary>
public sealed partial class MeasuringPointRow : ObservableObject
{
    private readonly Action<MeasuringPointRow> _onEdit;

    public MeasuringPointRow(MeasuringPoint point, int measurementCount, bool isReadOnly, Action<MeasuringPointRow> onEdit)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(onEdit);
        Id = point.Id;
        Name = point.Name;
        Location = point.Location;
        Zone = point.Zone;
        ZoneText = MeasurementLabels.Zone(point.Zone);
        MeasurementCount = measurementCount;
        CountText = measurementCount == 1 ? "1 Messung" : $"{measurementCount} Messungen";
        IsReadOnly = isReadOnly;
        _onEdit = onEdit;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string? Location { get; }

    public MeasurementZone Zone { get; }

    public string ZoneText { get; }

    public int MeasurementCount { get; }

    /// <summary>"3 Messungen".</summary>
    public string CountText { get; }

    public bool IsReadOnly { get; }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Edit()
    {
        if (CanEdit)
        {
            _onEdit(this);
        }
    }

    private bool CanEdit => !IsReadOnly;
}

/// <summary>One line of the weather log, newest first.</summary>
public sealed class WeatherRow
{
    public WeatherRow(WeatherObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Id = observation.Id;
        TimeText = Formatting.TimeOfDay(observation.ObservedAt);
        WindText = MeasurementLabels.Wind(observation.Wind);
        SpeedText = observation.WindSpeedKmh is { } speed ? Formatting.Measurement(speed, "km/h") : "–";
        TemperatureText = observation.TemperatureCelsius is { } temperature ? Formatting.Measurement(temperature, "°C") : "–";
        CloudsText = MeasurementLabels.Clouds(observation.Clouds);
        PrecipitationText = MeasurementLabels.Precipitation(observation.Precipitation);
        Source = observation.Source;
    }

    public Guid Id { get; }

    public string TimeText { get; }

    /// <summary>"W", "NO", "umlaufend".</summary>
    public string WindText { get; }

    public string SpeedText { get; }

    public string TemperatureText { get; }

    public string CloudsText { get; }

    public string PrecipitationText { get; }

    public string? Source { get; }
}

/// <summary>
/// One Messgröße in the Messgrößen list. AKTIV writes straight through; ENTFERNEN is offered only
/// while no Messung has a value of it, after that it can only be deactivated.
/// </summary>
public sealed partial class MeasurementChannelRow : ObservableObject
{
    private readonly Action<MeasurementChannelRow, bool> _onActiveChanged;
    private readonly Action<MeasurementChannelRow> _onRemove;

    public MeasurementChannelRow(
        MeasurementChannel channel,
        string? deviceName,
        bool hasReadings,
        bool isReadOnly,
        Action<MeasurementChannelRow, bool> onActiveChanged,
        Action<MeasurementChannelRow> onRemove)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(onActiveChanged);
        ArgumentNullException.ThrowIfNull(onRemove);
        Id = channel.Id;
        Name = channel.Name;
        Unit = channel.Unit;
        DirectionText = MeasurementLabels.Direction(channel.Definition.Direction);
        BandsText = MeasurementLabels.Bands(channel.Definition);
        DeviceName = deviceName;
        Note = channel.Note;
        HasReadings = hasReadings;
        IsReadOnly = isReadOnly;
        _isActive = channel.IsActive;
        _onActiveChanged = onActiveChanged;
        _onRemove = onRemove;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Unit { get; }

    /// <summary>"steigend" or "fallend".</summary>
    public string DirectionText { get; }

    /// <summary>"Erhöht ≥ 20 · Gefährlich ≥ 60 · Lebensgefährlich ≥ 200 ppm".</summary>
    public string BandsText { get; }

    public string? DeviceName { get; }

    public string? Note { get; }

    public bool HasReadings { get; }

    public bool IsReadOnly { get; }

    /// <summary>Whether the Messgröße is still measured: only an active one gets an input in the form.</summary>
    [ObservableProperty]
    private bool _isActive;

    partial void OnIsActiveChanged(bool value)
    {
        if (!IsReadOnly)
        {
            _onActiveChanged(this, value);
        }
    }

    /// <summary>Why ENTFERNEN is not offered, or null while it is.</summary>
    public string? RemoveHint => HasReadings ? "Hat Messwerte – nur deaktivieren möglich" : null;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove()
    {
        if (CanRemove)
        {
            _onRemove(this);
        }
    }

    private bool CanRemove => !IsReadOnly && !HasReadings;
}

/// <summary>One Messgerät in the Geräte list.</summary>
public sealed partial class MeasuringDeviceRow : ObservableObject
{
    private readonly Action<MeasuringDeviceRow> _onEdit;
    private readonly Action<MeasuringDeviceRow> _onRemove;

    public MeasuringDeviceRow(MeasuringDevice device, bool isReadOnly, Action<MeasuringDeviceRow> onEdit, Action<MeasuringDeviceRow> onRemove)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(onEdit);
        ArgumentNullException.ThrowIfNull(onRemove);
        Id = device.Id;
        Kind = device.Kind;
        KindText = MeasurementLabels.DeviceKind(device.Kind);
        Name = device.Name;
        Number = device.Number;
        Vehicle = device.Vehicle;
        Check = device.Check;
        IsReadOnly = isReadOnly;
        _onEdit = onEdit;
        _onRemove = onRemove;
    }

    public Guid Id { get; }

    public DeviceKind Kind { get; }

    /// <summary>"Mehrgasmessgerät".</summary>
    public string KindText { get; }

    public string Name { get; }

    /// <summary>The Geräte-Nr. or Charge.</summary>
    public string? Number { get; }

    public string? Vehicle { get; }

    /// <summary>The Prüfung or Haltbarkeit.</summary>
    public string? Check { get; }

    public bool IsReadOnly { get; }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Edit()
    {
        if (CanChange)
        {
            _onEdit(this);
        }
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void Remove()
    {
        if (CanChange)
        {
            _onRemove(this);
        }
    }

    private bool CanChange => !IsReadOnly;
}

/// <summary>
/// One Messgröße's input in the entry form: "12,5" or a range "8–14". The text survives a refresh
/// from another device, so a value half typed is never lost.
/// </summary>
public sealed partial class MeasurementValueInput : ObservableObject
{
    public MeasurementValueInput(Guid? channelId, GasDefinition definition, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ChannelId = channelId;
        Name = definition.Name;
        Unit = definition.Unit;
        Label = definition.Unit.Length == 0 ? definition.Name : $"{definition.Name} ({definition.Unit})";
        _text = text;
    }

    /// <summary>The Messgröße, or null while the Einsatz has none yet and this input stands for a
    /// Stammdaten Messgas the first Messpunkt will copy in.</summary>
    public Guid? ChannelId { get; }

    public string Name { get; }

    public string Unit { get; }

    /// <summary>"CO (ppm)".</summary>
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _text;

    /// <summary>Why the text is not a reading, once the form was submitted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;
}

/// <summary>A <see cref="MeasurementZone"/> with its label, for a picker.</summary>
public readonly record struct ZoneOption(MeasurementZone Value, string Label);

/// <summary>A <see cref="ProtectiveEquipment"/> with its label, for a picker.</summary>
public readonly record struct ProtectionOption(ProtectiveEquipment Value, string Label);

/// <summary>A <see cref="MeasurementMode"/> with its label, for a picker.</summary>
public readonly record struct ModeOption(MeasurementMode Value, string Label);

/// <summary>A <see cref="MeasurementStatus"/> with its label, for the Bewertung's radio buttons.</summary>
public readonly record struct StatusOption(MeasurementStatus Value, string Label);

/// <summary>A <see cref="WindDirection"/> with its label, for a picker.</summary>
public readonly record struct WindOption(WindDirection Value, string Label);

/// <summary>A <see cref="CloudCover"/> with its label, for a picker.</summary>
public readonly record struct CloudOption(CloudCover Value, string Label);

/// <summary>A <see cref="Precipitation"/> with its label, for a picker.</summary>
public readonly record struct PrecipitationOption(Precipitation Value, string Label);

/// <summary>A <see cref="DeviceKind"/> with its label, for a picker.</summary>
public readonly record struct DeviceKindOption(DeviceKind Value, string Label);

/// <summary>One entry of the Messpunkt filter: "Alle" (a null id) or one Messpunkt.</summary>
public sealed record PointFilterOption(Guid? PointId, string Label);

/// <summary>A Messgerät a Messgröße can be read from, or "ohne Gerät" (a null id).</summary>
public sealed record DeviceChoice(Guid? DeviceId, string Label);

/// <summary>A Stammdaten Messgas the Einsatz does not measure yet, offered as a Messgröße.</summary>
public sealed record GasChoice(GasDefinition Definition, string Label);

/// <summary>The head of one Messgröße's column in the Messreihe: "CO", "ppm", "X-am 5000".</summary>
public sealed record MeasurementColumn(Guid ChannelId, string Name, string Unit, string? DeviceName, bool IsActive);
