namespace Feuerwehr.Domain.Atemschutz;

/// <summary>
/// A breathing-apparatus (SCBA) team under monitoring. Lifecycle: <b>registered</b> (announced
/// but not yet under air) → <b>active</b> (clock running from <see cref="StartTime"/>) →
/// <b>returned</b> (<see cref="ExitTime"/> set). Registration does not start the clock, because a
/// standby/second Trupp may wait minutes before it actually starts consuming air.
///
/// Live countdown/alarm values are pure functions of a supplied <c>now</c>, anchored on
/// <see cref="StartTime"/>, so nothing time-derived is stored — reopening an incident resumes an
/// active Trupp's countdown from its persisted start time.
/// </summary>
public sealed class AtemschutzTrupp
{
    public const int DefaultMaxDurationMinutes = 30;

    /// <summary>
    /// Default operational time for a <see cref="ChemicalTruppDesignation"/>: a chemical-suit Trupp
    /// works to a shorter limit than an ordinary AGT, so it defaults lower than
    /// <see cref="DefaultMaxDurationMinutes"/> rather than sharing it.
    /// </summary>
    public const int DefaultChemicalMaxDurationMinutes = 20;
    public const int DefaultReturnPressureBar = 60;
    public const int DefaultPressureControlIntervalMinutes = 5;
    public const int MaxPressureBar = 400;

    /// <summary>
    /// The Trupp type that operates in chemical protection suits and is crewed by three rather
    /// than two. Named here rather than duplicated as a literal in the ViewModel, the seed and the
    /// tests, because the cardinality rule keys off it.
    /// </summary>
    public const string ChemicalTruppDesignation = "CSA-Trupp";

    /// <summary>Crew size of an ordinary Trupp: Truppführer + Truppmann.</summary>
    public const int StandardMemberCount = 2;

    /// <summary>Crew size of a <see cref="ChemicalTruppDesignation"/>.</summary>
    public const int ChemicalMemberCount = 3;

    private readonly List<PressureReading> _readings = new();
    private readonly List<TruppMember> _members = new();

    private AtemschutzTrupp() { }

    public Guid Id { get; private init; }
    public string Designation { get; private init; } = string.Empty;

    /// <summary>
    /// The crew, in position order. Always <see cref="StandardMemberCount"/> people, or
    /// <see cref="ChemicalMemberCount"/> for a CSA-Trupp — a Trupp is never one person.
    /// </summary>
    public IReadOnlyList<TruppMember> Members => _members;

    /// <summary>
    /// The crew as one line, for the grid, the PDF and ETB entries. Replaces the free-text
    /// Members string this type used to store.
    /// </summary>
    public string MembersDisplay => string.Join(" / ", _members.Select(m => m.Name));

    public string? CallSign { get; private init; }
    public string? Task { get; private init; }

    /// <summary>When the Trupp was announced/registered (not yet necessarily under air).</summary>
    public DateTimeOffset RegisteredAt { get; private init; }

    /// <summary>When the Trupp went under air. Null while still on standby.</summary>
    public DateTimeOffset? StartTime { get; private set; }

    /// <summary>Cylinder pressure recorded the moment the Trupp went under air. Null until started.</summary>
    public int? StartPressure { get; private set; }

    public int MaxDurationMinutes { get; private init; }
    public int ReturnPressureBar { get; private init; }
    public int PressureControlIntervalMinutes { get; private init; }
    public DateTimeOffset? ExitTime { get; private set; }

    public IReadOnlyList<PressureReading> PressureReadings => _readings;

    public static AtemschutzTrupp Register(
        DateTimeOffset registeredAt,
        string designation,
        IEnumerable<TruppMember> members,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = DefaultMaxDurationMinutes,
        int returnPressureBar = DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = DefaultPressureControlIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (string.IsNullOrWhiteSpace(designation))
            throw new ArgumentException("Trupp-Bezeichnung darf nicht leer sein.", nameof(designation));
        var crew = members.ToList();
        ValidateCrew(designation, crew);
        ValidatePressure(returnPressureBar, nameof(returnPressureBar));
        if (maxDurationMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDurationMinutes));
        if (pressureControlIntervalMinutes <= 0)
            throw new ArgumentOutOfRangeException(nameof(pressureControlIntervalMinutes));

        var trupp = new AtemschutzTrupp
        {
            Id = Guid.NewGuid(),
            RegisteredAt = registeredAt,
            Designation = designation.Trim(),
            CallSign = string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            Task = string.IsNullOrWhiteSpace(task) ? null : task.Trim(),
            MaxDurationMinutes = maxDurationMinutes,
            ReturnPressureBar = returnPressureBar,
            PressureControlIntervalMinutes = pressureControlIntervalMinutes
        };
        trupp._members.AddRange(crew);
        return trupp;
    }

    /// <summary>True when this designation denotes a three-person chemical-protection Trupp.</summary>
    public static bool IsChemicalTrupp(string designation) =>
        string.Equals(designation?.Trim(), ChemicalTruppDesignation, StringComparison.OrdinalIgnoreCase);

    /// <summary>Required crew size for a designation: three for CSA, two otherwise.</summary>
    public static int RequiredMemberCount(string designation) =>
        IsChemicalTrupp(designation) ? ChemicalMemberCount : StandardMemberCount;

    private static void ValidateCrew(string designation, IReadOnlyList<TruppMember> crew)
    {
        // Atemschutz is never a solo activity -- a Trupp is the unit that goes under air together,
        // and the monitoring sheet has no concept of a single wearer. Enforcing the count here
        // rather than in the ViewModel keeps it true for rehydrated and imported data as well.
        var required = RequiredMemberCount(designation);
        if (crew.Count != required)
            throw new ArgumentException(
                $"{designation.Trim()} muss aus genau {required} Personen bestehen (angegeben: {crew.Count}).",
                nameof(crew));
        if (crew.Any(m => m is null || string.IsNullOrWhiteSpace(m.Name)))
            throw new ArgumentException("Alle Truppmitglieder müssen einen Namen haben.", nameof(crew));
        if (crew.Select(m => m.Role).Distinct().Count() != crew.Count)
            throw new ArgumentException("Jede Truppfunktion darf nur einmal besetzt sein.", nameof(crew));
    }

    public static AtemschutzTrupp Rehydrate(
        Guid id,
        DateTimeOffset registeredAt,
        DateTimeOffset? startTime,
        string designation,
        IEnumerable<TruppMember> members,
        string? callSign,
        string? task,
        int? startPressure,
        int maxDurationMinutes,
        int returnPressureBar,
        int pressureControlIntervalMinutes,
        DateTimeOffset? exitTime,
        IEnumerable<PressureReading> readings)
    {
        var trupp = new AtemschutzTrupp
        {
            Id = id,
            RegisteredAt = registeredAt,
            StartTime = startTime,
            Designation = designation,
            CallSign = callSign,
            Task = task,
            StartPressure = startPressure,
            MaxDurationMinutes = maxDurationMinutes,
            ReturnPressureBar = returnPressureBar,
            PressureControlIntervalMinutes = pressureControlIntervalMinutes,
            ExitTime = exitTime
        };
        // Rehydrate deliberately does not re-run ValidateCrew: a stored Trupp is history, and
        // refusing to open an incident because an old record has the wrong crew size would make
        // the file unreadable rather than merely imperfect.
        trupp._members.AddRange(members);
        trupp._readings.AddRange(readings);
        return trupp;
    }

    /// <summary>Sends the Trupp under air: starts the clock and records the starting pressure.</summary>
    public void Start(DateTimeOffset time, int startPressure)
    {
        if (HasStarted)
            throw new InvalidOperationException("Trupp ist bereits unter Atemschutz.");
        ValidatePressure(startPressure, nameof(startPressure));
        if (startPressure <= 0)
            throw new ArgumentOutOfRangeException(nameof(startPressure), "Einstiegsdruck muss größer als 0 sein.");
        StartTime = time;
        StartPressure = startPressure;
    }

    public void RecordPressure(DateTimeOffset time, int bar)
    {
        if (!IsActive)
            throw new InvalidOperationException("Druckkontrolle nur für einen Trupp unter Atemschutz möglich.");
        ValidatePressure(bar, nameof(bar));
        _readings.Add(new PressureReading(time, bar));
    }

    public void MarkReturned(DateTimeOffset time)
    {
        if (!HasStarted)
            throw new InvalidOperationException("Trupp ist noch nicht unter Atemschutz.");
        if (ExitTime is not null)
            throw new InvalidOperationException("Trupp ist bereits zurück.");
        ExitTime = time;
    }

    public bool HasStarted => StartTime is not null;

    /// <summary>Registered but not yet under air.</summary>
    public bool IsWaiting => StartTime is null && ExitTime is null;

    /// <summary>Under air right now (started and not yet returned).</summary>
    public bool IsActive => StartTime is not null && ExitTime is null;

    public bool IsReturned => ExitTime is not null;

    /// <summary>Most recent measured pressure, or the start pressure if none recorded yet; null before start.</summary>
    public int? LatestPressure => _readings.Count > 0 ? _readings[^1].Bar : StartPressure;

    public DateTimeOffset? DueAt =>
        StartTime is { } start ? start + TimeSpan.FromMinutes(MaxDurationMinutes) : null;

    /// <summary>
    /// Time under air. Once the Trupp is back this is a closed fact, so it is measured to
    /// <see cref="ExitTime"/> rather than to <paramref name="now"/> — otherwise a Trupp that
    /// returned months ago keeps accumulating and the grid reads tens of thousands of hours.
    /// </summary>
    public TimeSpan Elapsed(DateTimeOffset now) =>
        StartTime is { } start ? (ExitTime ?? now) - start : TimeSpan.Zero;

    public TimeSpan Remaining(DateTimeOffset now) =>
        DueAt is { } due ? due - now : TimeSpan.Zero;

    public bool IsTimeAlarm(DateTimeOffset now) => IsActive && DueAt is { } due && now >= due;

    public bool IsPressureAlarm => IsActive && LatestPressure is { } p && p <= ReturnPressureBar;

    public bool IsAlarm(DateTimeOffset now) => IsTimeAlarm(now) || IsPressureAlarm;

    /// <summary>Anchor for the next pressure check: the latest reading, else the start time.</summary>
    private DateTimeOffset? LastControlAt =>
        _readings.Count > 0 ? _readings[^1].Time : StartTime;

    public DateTimeOffset? NextControlDueAt =>
        LastControlAt is { } anchor ? anchor + TimeSpan.FromMinutes(PressureControlIntervalMinutes) : null;

    public TimeSpan ControlRemaining(DateTimeOffset now) =>
        NextControlDueAt is { } due ? due - now : TimeSpan.Zero;

    public bool IsControlDue(DateTimeOffset now) =>
        IsActive && NextControlDueAt is { } due && now >= due;

    private static void ValidatePressure(int bar, string paramName)
    {
        if (bar < 0 || bar > MaxPressureBar)
            throw new ArgumentOutOfRangeException(paramName, $"Druck muss zwischen 0 und {MaxPressureBar} bar liegen.");
    }
}
