namespace LageBuch.Domain.Atemschutz;

/// <summary>
/// A breathing-apparatus (SCBA) team under monitoring. Lifecycle: <b>registered</b>/Bereitgestellt
/// (announced but not yet under air) → <b>active</b>/Im Einsatz (clock running from
/// <see cref="StartTime"/>) → <b>withdrawing</b>/Rückzug (<see cref="WithdrawTime"/> set) →
/// <b>removed</b>/Abgenommen (<see cref="ExitTime"/> set). Registration does not start the clock,
/// because a standby/second Trupp may wait minutes before it actually starts consuming air.
///
/// Live countdown/alarm values are pure functions of a supplied <c>now</c>, anchored on
/// <see cref="StartTime"/>, so nothing time-derived is stored — reopening an incident resumes an
/// active Trupp's countdown from its persisted start time.
/// </summary>
public sealed class AtemschutzTrupp
{
    /// <summary>
    /// Fallback Einsatzzeit for a Trupp whose type the Stammdaten do not list. Every listed type
    /// carries its own <c>MaxDurationMinutes</c>; this is only what "no type at all" means.
    /// </summary>
    public const int DefaultMaxDurationMinutes = 30;

    /// <summary>
    /// The largest Einsatzzeit the spinners offer, in minutes. A UI affordance, not an Atemschutz
    /// rule: the stored value is floored but deliberately not capped, so this only has to be
    /// generous enough to cover a real long-duration apparatus (four hours) and, above all, to be
    /// the <em>same</em> in the Stammdaten editor and on the Atemschutz form. They disagreed once --
    /// the editor offered 180 while the form stopped at 120 -- which let a brigade configure a
    /// Trupp-Typ it could then not register at its own Einsatzzeit.
    /// </summary>
    public const int MaxEditableDurationMinutes = 240;

    public const int DefaultReturnPressureBar = 50;
    public const int DefaultPressureControlIntervalMinutes = 5;
    public const int MaxPressureBar = 400;

    /// <summary>
    /// Crew size of an ordinary Trupp: Truppführer + Truppmann. Also the minimum — Atemschutz is
    /// never a solo activity.
    /// </summary>
    public const int StandardMemberCount = 2;

    /// <summary>
    /// The largest crew a Trupp can have, which is not a policy choice but a consequence of
    /// <see cref="TruppRole"/> having exactly that many positions: the monitoring sheet has
    /// nowhere to write a fourth name, and the role ordinals are a storage contract.
    /// <para>
    /// How many people a <em>particular</em> Trupp type needs is Stammdaten (a Trupp-Typ's
    /// <c>MemberCount</c>), not a constant here — see issue #398. This class validates only what
    /// it can prove without the Stammdaten it cannot see.
    /// </para>
    /// </summary>
    public const int MaxMemberCount = 3;

    private readonly List<PressureReading> _readings = new();
    private readonly List<TruppMember> _members = new();

    private AtemschutzTrupp()
    {
    }

    public Guid Id { get; private init; }

    public int TruppNumber { get; private init; }

    public string Designation { get; private init; } = string.Empty;

    /// <summary>"Trupp {N} ({Designation})" — the display form used in the grid, ETB text, the
    /// PDF export and the header timer banners. Kept here as the single source of truth so the
    /// number and the type name are never composed differently in two places.</summary>
    public string DisplayName => FormatDisplayName(TruppNumber, Designation);

    /// <summary>Same formatting as <see cref="DisplayName"/>, usable before an instance exists —
    /// e.g. to compose the registration ETB line from form inputs, before the mutation commits.</summary>
    public static string FormatDisplayName(int truppNumber, string designation) => $"Trupp {truppNumber} ({designation})";

    /// <summary>
    /// The crew, in position order: between <see cref="StandardMemberCount"/> and
    /// <see cref="MaxMemberCount"/> people, as the Trupp-Typ in the Stammdaten calls for.
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

    /// <summary>Cylinder pressure recorded at Bereitstellen, before the Trupp goes under air.
    /// Null only for a legacy row registered before this field existed.</summary>
    public int? EntryPressure { get; private set; }

    public int MaxDurationMinutes { get; private init; }

    public int ReturnPressureBar { get; private init; }

    public int PressureControlIntervalMinutes { get; private init; }

    /// <summary>When the Trupp began its Rückzug. Null before withdrawing, and while still waiting/active.</summary>
    public DateTimeOffset? WithdrawTime { get; private set; }

    /// <summary>When the Trupp was abgenommen (mask off, monitoring finished).</summary>
    public DateTimeOffset? ExitTime { get; private set; }

    public IReadOnlyList<PressureReading> PressureReadings => _readings;

    public static AtemschutzTrupp Register(
        DateTimeOffset registeredAt,
        string designation,
        IEnumerable<TruppMember> members,
        int entryPressure,
        int truppNumber,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = DefaultMaxDurationMinutes,
        int returnPressureBar = DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = DefaultPressureControlIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (string.IsNullOrWhiteSpace(designation))
        {
            throw new ArgumentException("Trupp-Bezeichnung darf nicht leer sein.", nameof(designation));
        }

        var crew = members.ToList();
        ValidateCrew(designation, crew);
        ValidatePressure(entryPressure, nameof(entryPressure));
        if (entryPressure <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryPressure), "Einstiegsdruck muss größer als 0 sein.");
        }

        ValidatePressure(returnPressureBar, nameof(returnPressureBar));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDurationMinutes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pressureControlIntervalMinutes);

        var trupp = new AtemschutzTrupp
        {
            Id = Guid.NewGuid(),
            TruppNumber = truppNumber,
            RegisteredAt = registeredAt,
            Designation = designation.Trim(),
            EntryPressure = entryPressure,
            CallSign = string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            Task = string.IsNullOrWhiteSpace(task) ? null : task.Trim(),
            MaxDurationMinutes = maxDurationMinutes,
            ReturnPressureBar = returnPressureBar,
            PressureControlIntervalMinutes = pressureControlIntervalMinutes,
        };
        trupp._members.AddRange(crew);
        return trupp;
    }

    private static void ValidateCrew(string designation, List<TruppMember> crew)
    {
        // Atemschutz is never a solo activity -- a Trupp is the unit that goes under air together,
        // and the monitoring sheet has no concept of a single wearer, nor room for a fourth name.
        //
        // How many people a *particular* type needs used to be decided here, by comparing the
        // designation against the literal "CSA-Trupp" -- which quietly stopped applying the moment
        // a brigade renamed the type in its Stammdaten (#398). That rule now lives on the Trupp-Typ
        // itself and is enforced where the Stammdaten are readable, in ScbaViewModel. What is left
        // here is what this class can prove on its own, for registered and imported data alike.
        if (crew.Count < StandardMemberCount || crew.Count > MaxMemberCount)
        {
            throw new ArgumentException(
                $"{designation.Trim()} muss aus {StandardMemberCount} bis {MaxMemberCount} Personen bestehen (angegeben: {crew.Count}).",
                nameof(crew));
        }

        if (crew.Any(m => m is null || string.IsNullOrWhiteSpace(m.Name)))
        {
            throw new ArgumentException("Alle Truppmitglieder müssen einen Namen haben.", nameof(crew));
        }

        // Contiguous from Truppfuehrer, not merely distinct: a two-person crew of
        // {Truppfuehrer, ZweiterTruppmann} leaves a hole at Truppmann, and a sheet with a gap in
        // the middle is one nobody can read back.
        if (!crew.Select(m => (int)m.Role).OrderBy(r => r).SequenceEqual(Enumerable.Range(0, crew.Count)))
        {
            throw new ArgumentException(
                "Die Truppfunktionen müssen lückenlos ab Truppführer besetzt sein, jede genau einmal.",
                nameof(crew));
        }
    }

    public static AtemschutzTrupp Rehydrate(
        Guid id,
        int truppNumber,
        DateTimeOffset registeredAt,
        DateTimeOffset? startTime,
        DateTimeOffset? withdrawTime,
        string designation,
        IEnumerable<TruppMember> members,
        string? callSign,
        string? task,
        int? entryPressure,
        int maxDurationMinutes,
        int returnPressureBar,
        int pressureControlIntervalMinutes,
        DateTimeOffset? exitTime,
        IEnumerable<PressureReading> readings)
    {
        var trupp = new AtemschutzTrupp
        {
            Id = id,
            TruppNumber = truppNumber,
            RegisteredAt = registeredAt,
            StartTime = startTime,
            WithdrawTime = withdrawTime,
            Designation = designation,
            CallSign = callSign,
            Task = task,
            EntryPressure = entryPressure,
            MaxDurationMinutes = maxDurationMinutes,
            ReturnPressureBar = returnPressureBar,
            PressureControlIntervalMinutes = pressureControlIntervalMinutes,
            ExitTime = exitTime,
        };

        // Rehydrate deliberately does not re-run ValidateCrew: a stored Trupp is history, and
        // refusing to open an incident because an old record has the wrong crew size would make
        // the file unreadable rather than merely imperfect.
        trupp._members.AddRange(members);
        trupp._readings.AddRange(readings);
        return trupp;
    }

    /// <summary>Sends the Trupp under air: starts the clock. The entry pressure was already
    /// recorded at <see cref="Register"/> time.</summary>
    public void Start(DateTimeOffset time)
    {
        if (HasStarted)
        {
            throw new InvalidOperationException("Trupp ist bereits unter Atemschutz.");
        }

        StartTime = time;
    }

    public void RecordPressure(DateTimeOffset time, int bar)
    {
        if (!(IsActive || IsWithdrawing))
        {
            throw new InvalidOperationException("Druckkontrolle nur für einen Trupp unter Atemschutz möglich.");
        }

        ValidatePressure(bar, nameof(bar));
        _readings.Add(new PressureReading(time, bar));
    }

    /// <summary>Begins the Trupp's Rückzug — still under air, on the way out.</summary>
    public void Withdraw(DateTimeOffset time)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Rückzug nur für einen Trupp im Einsatz möglich.");
        }

        WithdrawTime = time;
    }

    /// <summary>Marks the Trupp abgenommen (mask off) — reachable only after Rückzug.</summary>
    public void MarkRemoved(DateTimeOffset time)
    {
        if (WithdrawTime is null)
        {
            throw new InvalidOperationException("Trupp muss zuerst den Rückzug antreten.");
        }

        if (ExitTime is not null)
        {
            throw new InvalidOperationException("Trupp ist bereits abgenommen.");
        }

        ExitTime = time;
    }

    public bool HasStarted => StartTime is not null;

    /// <summary>Registered but not yet under air.</summary>
    public bool IsWaiting => StartTime is null && ExitTime is null;

    /// <summary>Im Einsatz: under air, not yet withdrawing.</summary>
    public bool IsActive => StartTime is not null && WithdrawTime is null && ExitTime is null;

    /// <summary>Rückzug: still under air, on the way back.</summary>
    public bool IsWithdrawing => WithdrawTime is not null && ExitTime is null;

    /// <summary>Abgenommen: monitoring finished.</summary>
    public bool IsReturned => ExitTime is not null;

    /// <summary>Most recent measured pressure, or the entry pressure if none recorded yet; null for a legacy row with neither.</summary>
    public int? LatestPressure => _readings.Count > 0 ? _readings[^1].Bar : EntryPressure;

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

    // Alarms and pressure-control reminders stay live through Rückzug: a withdrawing crew is
    // still consuming air and still needs monitoring, and Rückzug/Abgenommen are separate manual
    // steps the operator takes independently of the alarm banner.
    public bool IsTimeAlarm(DateTimeOffset now) => (IsActive || IsWithdrawing) && DueAt is { } due && now >= due;

    public bool IsPressureAlarm => (IsActive || IsWithdrawing) && LatestPressure is { } p && p <= ReturnPressureBar;

    public bool IsAlarm(DateTimeOffset now) => IsTimeAlarm(now) || IsPressureAlarm;

    /// <summary>Anchor for the next pressure check: the latest reading, else the start time.</summary>
    private DateTimeOffset? LastControlAt =>
        _readings.Count > 0 ? _readings[^1].Time : StartTime;

    public DateTimeOffset? NextControlDueAt =>
        LastControlAt is { } anchor ? anchor + TimeSpan.FromMinutes(PressureControlIntervalMinutes) : null;

    public TimeSpan ControlRemaining(DateTimeOffset now) =>
        NextControlDueAt is { } due ? due - now : TimeSpan.Zero;

    public bool IsControlDue(DateTimeOffset now) =>
        (IsActive || IsWithdrawing) && NextControlDueAt is { } due && now >= due;

    private static void ValidatePressure(int bar, string paramName)
    {
        if (bar < 0 || bar > MaxPressureBar)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Druck muss zwischen 0 und {MaxPressureBar} bar liegen.");
        }
    }
}
