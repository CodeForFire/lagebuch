namespace Feuerwehr.Domain.Atemschutz;

/// <summary>
/// A breathing-apparatus (SCBA) team under monitoring. Mutable: pressure readings are
/// appended and the exit time is set when the team returns. Live countdown/alarm values are
/// pure functions of a supplied <c>now</c>, so nothing time-derived is stored — reopening an
/// incident resumes an active Trupp's countdown from its persisted entry time.
/// </summary>
public sealed class AtemschutzTrupp
{
    public const int DefaultMaxDurationMinutes = 30;
    public const int DefaultReturnPressureBar = 60;
    public const int MaxPressureBar = 400;

    private readonly List<PressureReading> _readings = new();

    private AtemschutzTrupp() { }

    public Guid Id { get; private init; }
    public string Designation { get; private init; } = string.Empty;
    public string Members { get; private init; } = string.Empty;
    public string? CallSign { get; private init; }
    public string? Task { get; private init; }
    public DateTimeOffset EntryTime { get; private init; }
    public int EntryPressure { get; private init; }
    public int MaxDurationMinutes { get; private init; }
    public int ReturnPressureBar { get; private init; }
    public DateTimeOffset? ExitTime { get; private set; }

    public IReadOnlyList<PressureReading> PressureReadings => _readings;

    public static AtemschutzTrupp Create(
        DateTimeOffset entryTime,
        string designation,
        string members,
        int entryPressure,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = DefaultMaxDurationMinutes,
        int returnPressureBar = DefaultReturnPressureBar)
    {
        if (string.IsNullOrWhiteSpace(designation))
            throw new ArgumentException("Trupp-Bezeichnung darf nicht leer sein.", nameof(designation));
        if (string.IsNullOrWhiteSpace(members))
            throw new ArgumentException("Mannschaft darf nicht leer sein.", nameof(members));
        ValidatePressure(entryPressure, nameof(entryPressure));
        ValidatePressure(returnPressureBar, nameof(returnPressureBar));
        if (maxDurationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDurationMinutes));

        return new AtemschutzTrupp
        {
            Id = Guid.NewGuid(),
            EntryTime = entryTime,
            Designation = designation.Trim(),
            Members = members.Trim(),
            EntryPressure = entryPressure,
            CallSign = string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            Task = string.IsNullOrWhiteSpace(task) ? null : task.Trim(),
            MaxDurationMinutes = maxDurationMinutes,
            ReturnPressureBar = returnPressureBar
        };
    }

    public static AtemschutzTrupp Rehydrate(
        Guid id,
        DateTimeOffset entryTime,
        string designation,
        string members,
        string? callSign,
        string? task,
        int entryPressure,
        int maxDurationMinutes,
        int returnPressureBar,
        DateTimeOffset? exitTime,
        IEnumerable<PressureReading> readings)
    {
        var trupp = new AtemschutzTrupp
        {
            Id = id,
            EntryTime = entryTime,
            Designation = designation,
            Members = members,
            CallSign = callSign,
            Task = task,
            EntryPressure = entryPressure,
            MaxDurationMinutes = maxDurationMinutes,
            ReturnPressureBar = returnPressureBar,
            ExitTime = exitTime
        };
        trupp._readings.AddRange(readings);
        return trupp;
    }

    public void RecordPressure(DateTimeOffset time, int bar)
    {
        if (!IsActive)
            throw new InvalidOperationException("Trupp ist bereits zurück; keine Druckkontrolle möglich.");
        ValidatePressure(bar, nameof(bar));
        _readings.Add(new PressureReading(time, bar));
    }

    public void MarkReturned(DateTimeOffset time)
    {
        if (!IsActive)
            throw new InvalidOperationException("Trupp ist bereits zurück.");
        ExitTime = time;
    }

    public bool IsActive => ExitTime is null;

    /// <summary>Most recent measured pressure, or the entry pressure if none recorded yet.</summary>
    public int LatestPressure => _readings.Count > 0 ? _readings[^1].Bar : EntryPressure;

    public DateTimeOffset DueAt => EntryTime + TimeSpan.FromMinutes(MaxDurationMinutes);

    public TimeSpan Elapsed(DateTimeOffset now) => now - EntryTime;

    public TimeSpan Remaining(DateTimeOffset now) => DueAt - now;

    public bool IsTimeAlarm(DateTimeOffset now) => IsActive && now >= DueAt;

    public bool IsPressureAlarm => IsActive && LatestPressure <= ReturnPressureBar;

    public bool IsAlarm(DateTimeOffset now) => IsTimeAlarm(now) || IsPressureAlarm;

    private static void ValidatePressure(int bar, string paramName)
    {
        if (bar < 0 || bar > MaxPressureBar)
            throw new ArgumentOutOfRangeException(paramName, $"Druck muss zwischen 0 und {MaxPressureBar} bar liegen.");
    }
}
