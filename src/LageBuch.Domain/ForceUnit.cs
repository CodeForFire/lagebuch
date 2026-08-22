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
            throw new ArgumentException("Feuerwehr darf nicht leer sein.", nameof(brigade));
        if (personnelCount < 0)
            throw new ArgumentOutOfRangeException(nameof(personnelCount));
        if (scbaCount < 0)
            throw new ArgumentOutOfRangeException(nameof(scbaCount));
        // Atemschutzgeräteträger are a subset of the crew, so they can never outnumber it. Worth
        // enforcing rather than merely displaying: this count is what tells the Einsatzleiter how
        // many Trupps can actually be formed.
        if (scbaCount > personnelCount)
            throw new ArgumentOutOfRangeException(nameof(scbaCount),
                "Atemschutzgeräteträger dürfen die Gesamtstärke nicht übersteigen.");
        if (officerCount < 0)
            throw new ArgumentOutOfRangeException(nameof(officerCount));
        // Führungskräfte are likewise a subset of the crew (#76).
        if (officerCount > personnelCount)
            throw new ArgumentOutOfRangeException(nameof(officerCount),
                "Führungskräfte dürfen die Gesamtstärke nicht übersteigen.");

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
    /// to Im Einsatz, and the Bemerkung is where the Einsatzleiter notes why. Everything else --
    /// which LageBuch, how many people, how many AGT -- is a fact about what was alarmed, so it
    /// is deliberately not settable here; a wrong crew size means the row was entered wrong and
    /// belongs to a correction path, not to routine status keeping.
    /// </summary>
    public ForceUnit WithStatusAndNotes(string? status, string? notes) => this with
    {
        Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
    };
}
