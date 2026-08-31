namespace LageBuch.Domain;

public sealed record ForceUnit(
    Guid Id,
    string Brigade,
    string? CallSign,
    int PersonnelCount,
    int ScbaCount,
    string? Status,
    string? Notes,

    // Appended last with a default so pre-#76 construction sites (repository, snapshot, sync) keep
    // compiling — and old rows/payloads read as "keine Führungskraft erfasst" (0/x/x) instead of
    // breaking. The total is unchanged by this field: Mannschaft is derived, not stored.
    int OfficerCount = 0)
{
    /// <summary>Mannschaft = Gesamtstärke abzüglich der Führungskräfte.</summary>
    public int MannschaftCount => PersonnelCount - OfficerCount;

    /// <summary>The German Stärke notation: Führungskraft / Mannschaft / Gesamt ("1/1/2").</summary>
    public string StrengthText => $"{OfficerCount}/{MannschaftCount}/{PersonnelCount}";

    public static ForceUnit Create(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0)
    {
        if (string.IsNullOrWhiteSpace(brigade))
        {
            throw new ArgumentException("Feuerwehr darf nicht leer sein.", nameof(brigade));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(personnelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(scbaCount);

        // Atemschutzgeräteträger are a subset of the crew, so they can never outnumber it. Worth
        // enforcing rather than merely displaying: this count is what tells the Einsatzleiter how
        // many Trupps can actually be formed.
        if (scbaCount > personnelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scbaCount),
                "Atemschutzgeräteträger dürfen die Gesamtstärke nicht übersteigen.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(officerCount);

        // Führungskräfte are likewise a subset of the crew (#76).
        if (officerCount > personnelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(officerCount),
                "Führungskräfte dürfen die Gesamtstärke nicht übersteigen.");
        }

        return new ForceUnit(
            Guid.NewGuid(),
            brigade.Trim(),
            string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            personnelCount,
            scbaCount,
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            officerCount);
    }

    /// <summary>
    /// The two fields that move while the Einsatz runs: a unit goes from Alarmiert to Auf Anfahrt
    /// to Im Einsatz, and the Bemerkung is where the Einsatzleiter notes why. The Stärke counts are
    /// correctable through <see cref="WithStrength"/> — a deliberate #76 relaxation of the old
    /// "counts never move" rule, paid for with a full audit trail on the unit.
    /// </summary>
    public ForceUnit WithStatusAndNotes(string? status, string? notes) => this with
    {
        Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
    };

    public IReadOnlyList<ForceUnitStrengthEdit> Edits { get; private init; } =
        Array.Empty<ForceUnitStrengthEdit>();

    public static ForceUnit Rehydrate(
        Guid id,
        string brigade,
        string? callSign,
        int personnelCount,
        int scbaCount,
        string? status,
        string? notes,
        int officerCount,
        IEnumerable<ForceUnitStrengthEdit>? edits = null)
        => new(id, brigade, callSign, personnelCount, scbaCount, status, notes, officerCount)
        {
            Edits = (edits ?? Enumerable.Empty<ForceUnitStrengthEdit>()).ToList(),
        };

    /// <summary>
    /// Corrects the three Stärke-Zahlen (Führungskraft / Mannschaft via Gesamt / AGT), retaining
    /// the prior values under <see cref="Edits"/> — same discipline as
    /// <see cref="Etb.EtbEntry.WithEditedText"/>: an entered Stärke is part of the incident record,
    /// so overwriting it silently is not an option. Resubmitting identical numbers returns this
    /// instance as-is rather than growing <see cref="Edits"/>, so retries and no-op writes cannot
    /// inflate the retained history. Validation mirrors <see cref="Create"/>.
    /// </summary>
    public ForceUnit WithStrength(
        int officerCount,
        int personnelCount,
        int scbaCount,
        SessionOperator editor,
        DateTimeOffset editedAt)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentOutOfRangeException.ThrowIfNegative(personnelCount);
        if (scbaCount < 0 || scbaCount > personnelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scbaCount),
                "Atemschutzgeräteträger dürfen die Gesamtstärke nicht übersteigen.");
        }

        if (officerCount < 0 || officerCount > personnelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(officerCount),
                "Führungskräfte dürfen die Gesamtstärke nicht übersteigen.");
        }

        if (officerCount == OfficerCount && personnelCount == PersonnelCount && scbaCount == ScbaCount)
        {
            return this;
        }

        var edits = new List<ForceUnitStrengthEdit>(Edits)
        {
            new(OfficerCount, PersonnelCount, ScbaCount, editor.Display, editedAt),
        };
        return this with
        {
            OfficerCount = officerCount,
            PersonnelCount = personnelCount,
            ScbaCount = scbaCount,
            Edits = edits,
        };
    }
}
