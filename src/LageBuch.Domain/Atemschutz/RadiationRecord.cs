namespace LageBuch.Domain.Atemschutz;

/// <summary>
/// The Strahlenschutzüberwachung of one Einsatzkraft in a Strahlenschutztrupp (#994): the numbers
/// of the two dosimeters, the Maske and the PA they carry, and the Alarmdosimeter's Ablesewert once
/// it is reported.
/// <para>
/// A dosimeter that is not there is an explicit answer, <see cref="OslDosimeterMissing"/> or
/// <see cref="AlarmDosimeterMissing"/>, never just an empty field: the record has to say whether
/// the chain of documentation broke or merely has not been filled in yet. A number can stand
/// beside "missing" -- a dosimeter lost under Atemschutz was still handed out under that number.
/// </para>
/// </summary>
public sealed record RadiationRecord
{
    /// <summary>The longest number kept for a dosimeter, Maske or PA.</summary>
    public const int MaxNumberLength = 50;

    public RadiationRecord(
        string? oslDosimeterNumber = null,
        bool oslDosimeterMissing = false,
        string? alarmDosimeterNumber = null,
        bool alarmDosimeterMissing = false,
        string? maskNumber = null,
        string? paNumber = null,
        DoseReading? alarmReading = null)
    {
        OslDosimeterNumber = Number(oslDosimeterNumber, nameof(oslDosimeterNumber));
        OslDosimeterMissing = oslDosimeterMissing;
        AlarmDosimeterNumber = Number(alarmDosimeterNumber, nameof(alarmDosimeterNumber));
        AlarmDosimeterMissing = alarmDosimeterMissing;
        MaskNumber = Number(maskNumber, nameof(maskNumber));
        PaNumber = Number(paNumber, nameof(paNumber));
        if (alarmDosimeterMissing && alarmReading is not null)
        {
            throw new ArgumentException(
                "Ein Alarmdosimeter, das nicht vorhanden ist, hat keinen Ablesewert.", nameof(alarmReading));
        }

        AlarmReading = alarmReading;
    }

    /// <summary>Nothing recorded yet: what a crew member of a Strahlenschutztrupp starts with.</summary>
    public static RadiationRecord Empty { get; } = new();

    public string? OslDosimeterNumber { get; }

    public bool OslDosimeterMissing { get; }

    public string? AlarmDosimeterNumber { get; }

    public bool AlarmDosimeterMissing { get; }

    public string? MaskNumber { get; }

    public string? PaNumber { get; }

    public DoseReading? AlarmReading { get; }

    /// <summary>Both dosimeters are accounted for: each has a number or is declared missing. What
    /// the Trupp needs before it goes under Atemschutz.</summary>
    public bool HasDosimeterNumbers =>
        (OslDosimeterMissing || OslDosimeterNumber is not null)
        && (AlarmDosimeterMissing || AlarmDosimeterNumber is not null);

    /// <summary>The Alarmdosimeter has its final answer: a reading, or "nicht vorhanden". What the
    /// Einsatz needs before it is closed.</summary>
    public bool HasAlarmAnswer => AlarmDosimeterMissing || AlarmReading is not null;

    /// <summary>
    /// A record from somewhere this app did not write it just now: a .lagebuch someone handed over,
    /// or a host's snapshot. Built leniently, the way Rehydrate treats a Trupp: one implausible value
    /// costs that value, never the whole Einsatz. A number over the cap is cut to it; a reading that
    /// is out of range or names no known unit is dropped, and so is one beside an Alarmdosimeter
    /// declared missing. A command a client sends goes through the checking constructor instead.
    /// </summary>
    public static RadiationRecord FromUntrusted(
        string? oslDosimeterNumber,
        bool oslDosimeterMissing,
        string? alarmDosimeterNumber,
        bool alarmDosimeterMissing,
        string? maskNumber,
        string? paNumber,
        decimal? doseValue,
        int? doseUnit) =>
        new(
            Capped(oslDosimeterNumber),
            oslDosimeterMissing,
            Capped(alarmDosimeterNumber),
            alarmDosimeterMissing,
            Capped(maskNumber),
            Capped(paNumber),
            alarmDosimeterMissing ? null : Dose(doseValue, doseUnit));

    /// <summary>"OSL-Dosimeter 4711, Alarmdosimeter 0815, Maske 12, PA 3", with "—" for an empty
    /// number and "(nicht vorhanden)" after a dosimeter declared missing. Shared by the ETB line and
    /// the Bericht so both say it the same way.</summary>
    public string NumbersDisplay =>
        $"OSL-Dosimeter {Dosimeter(OslDosimeterNumber, OslDosimeterMissing)}, "
        + $"Alarmdosimeter {Dosimeter(AlarmDosimeterNumber, AlarmDosimeterMissing)}, "
        + $"Maske {MaskNumber ?? "—"}, PA {PaNumber ?? "—"}";

    /// <summary>The Alarmdosimeter's answer for the Bericht: the reading, "nicht vorhanden", or
    /// "offen" while it is still owed.</summary>
    public string ReadingDisplay =>
        AlarmDosimeterMissing ? "Ablesewert: nicht vorhanden"
        : AlarmReading is { } reading ? $"Ablesewert {reading.Display}"
        : "Ablesewert offen";

    /// <summary>A dosimeter declared missing: the chain of documentation has a gap.</summary>
    public bool HasGap => OslDosimeterMissing || AlarmDosimeterMissing;

    /// <summary>This record with <paramref name="reading"/> as the Ablesewert, checked like any other.</summary>
    public RadiationRecord WithAlarmReading(DoseReading? reading) => new(
        OslDosimeterNumber, OslDosimeterMissing, AlarmDosimeterNumber, AlarmDosimeterMissing, MaskNumber, PaNumber, reading);

    private static string Dosimeter(string? number, bool missing) =>
        missing ? $"{number ?? "—"} (nicht vorhanden)" : number ?? "—";

    private static string? Capped(string? value) =>
        value is { Length: > MaxNumberLength } ? value[..MaxNumberLength] : value;

    private static DoseReading? Dose(decimal? value, int? unit) =>
        value is { } v && unit is { } u && Enum.IsDefined((DoseUnit)u) && v >= 0 && v <= DoseReading.MaxValue
            ? new DoseReading(v, (DoseUnit)u)
            : null;

    private static string? Number(string? value, string paramName)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is { Length: > MaxNumberLength })
        {
            throw new ArgumentException($"Eine Nummer darf höchstens {MaxNumberLength} Zeichen lang sein.", paramName);
        }

        return trimmed;
    }
}
