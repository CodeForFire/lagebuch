using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>Which of a crew member's two dosimeters a declaration is about (#994).</summary>
public enum DosimeterKind
{
    Osl,
    Alarm,
}

/// <summary>
/// One crew member in the Strahlenschutz dialog (#994): the four numbers, the two "nicht
/// vorhanden" declarations and the Ablesewert as typed. Declaring a dosimeter missing goes through
/// the dialog, which asks first; taking it back does not need to.
/// </summary>
public sealed partial class RadiationMemberRow : ObservableObject
{
    private readonly Action<RadiationMemberRow, DosimeterKind> _requestMissing;

    public RadiationMemberRow(TruppMember member, Action<RadiationMemberRow, DosimeterKind> requestMissing)
    {
        ArgumentNullException.ThrowIfNull(member);
        _requestMissing = requestMissing;
        Role = member.Role;
        Name = member.Name;
        RoleDisplay = member.RoleDisplay;
        var record = member.Radiation ?? RadiationRecord.Empty;
        _oslNumber = record.OslDosimeterNumber;
        _alarmNumber = record.AlarmDosimeterNumber;
        _maskNumber = record.MaskNumber;
        _paNumber = record.PaNumber;
        _oslMissing = record.OslDosimeterMissing;
        _alarmMissing = record.AlarmDosimeterMissing;
        _doseText = record.AlarmReading?.Value.ToString("0.###", Formatting.NumberFormat);

        // mSv unless the record says otherwise: the larger unit is what an Einsatz reports.
        _doseUnit = record.AlarmReading?.Unit ?? DoseUnit.MilliSievert;
    }

    /// <summary>The longest number the boxes take, for the view.</summary>
    public static int MaxNumberLength => RadiationRecord.MaxNumberLength;

    /// <summary>The units the picker offers, mSv first.</summary>
    public static IReadOnlyList<string> UnitOptions { get; } =
        [DoseReading.UnitLabel(DoseUnit.MilliSievert), DoseReading.UnitLabel(DoseUnit.MicroSievert)];

    public TruppRole Role { get; }

    public string Name { get; }

    public string RoleDisplay { get; }

    [ObservableProperty]
    private string? _oslNumber;

    [ObservableProperty]
    private string? _alarmNumber;

    [ObservableProperty]
    private string? _maskNumber;

    [ObservableProperty]
    private string? _paNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OslPresent))]
    private bool _oslMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlarmPresent))]
    private bool _alarmMissing;

    /// <summary>The Ablesewert as typed, "0,12" or "0.12"; blank while it is not reported yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DoseError))]
    private string? _doseText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DoseUnitLabel))]
    private DoseUnit _doseUnit;

    /// <summary>The picker's side of <see cref="DoseUnit"/>: "mSv" or "µSv".</summary>
    public string? DoseUnitLabel
    {
        get => DoseReading.UnitLabel(DoseUnit);
        set
        {
            if (value is not null)
            {
                DoseUnit = string.Equals(value, DoseReading.UnitLabel(DoseUnit.MicroSievert), StringComparison.Ordinal)
                    ? DoseUnit.MicroSievert
                    : DoseUnit.MilliSievert;
            }
        }
    }

    /// <summary>Why the typed Ablesewert cannot be taken, once SPEICHERN was pressed; null otherwise.</summary>
    [ObservableProperty]
    private string? _doseError;

    public bool OslPresent => !OslMissing;

    public bool AlarmPresent => !AlarmMissing;

    [RelayCommand]
    private void DeclareOslMissing() => _requestMissing(this, DosimeterKind.Osl);

    [RelayCommand]
    private void DeclareAlarmMissing() => _requestMissing(this, DosimeterKind.Alarm);

    [RelayCommand]
    private void UndoOslMissing() => OslMissing = false;

    [RelayCommand]
    private void UndoAlarmMissing() => AlarmMissing = false;

    /// <summary>Takes a confirmed declaration; a missing Alarmdosimeter has no Ablesewert.</summary>
    public void SetMissing(DosimeterKind kind, bool missing)
    {
        if (kind == DosimeterKind.Osl)
        {
            OslMissing = missing;
            return;
        }

        AlarmMissing = missing;
        if (missing)
        {
            DoseText = null;
        }
    }

    /// <summary>Checks the typed Ablesewert and says why it cannot be taken.</summary>
    public bool Validate()
    {
        DoseError = AlarmMissing || string.IsNullOrWhiteSpace(DoseText) || TryParseDose(DoseText, out _)
            ? null
            : "Ablesewert als Zahl eingeben, z. B. 0,12";
        return DoseError is null;
    }

    /// <summary>The record these fields describe. Call <see cref="Validate"/> first.</summary>
    public RadiationRecord ToRecord() => new(
        OslNumber,
        OslMissing,
        AlarmNumber,
        AlarmMissing,
        MaskNumber,
        PaNumber,
        !AlarmMissing && TryParseDose(DoseText, out var value) ? new DoseReading(value, DoseUnit) : null);

    /// <summary>
    /// A typed Ablesewert, with "," or "." as the decimal separator and no grouping: a display
    /// shows "0.12", the form shows "0,12", and both mean the same. Out of range is not a number
    /// either, so the field says what it wants rather than the save failing on it.
    /// </summary>
    public static bool TryParseDose(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return decimal.TryParse(
                   text.Trim().Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint,
                   CultureInfo.InvariantCulture,
                   out value)
               && value <= DoseReading.MaxValue;
    }
}
