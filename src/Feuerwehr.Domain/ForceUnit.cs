namespace Feuerwehr.Domain;

public sealed record ForceUnit(
    Guid Id,
    string Brigade,
    string? CallSign,
    int PersonnelCount,
    int ScbaCount,
    string? Status,
    string? Notes)
{
    public static ForceUnit Create(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0)
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

        return new ForceUnit(
            Guid.NewGuid(),
            brigade.Trim(),
            string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            personnelCount,
            scbaCount,
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }

    /// <summary>
    /// The two fields that move while the Einsatz runs: a unit goes from Alarmiert to Auf Anfahrt
    /// to Im Einsatz, and the Bemerkung is where the Einsatzleiter notes why. Everything else --
    /// which Feuerwehr, how many people, how many AGT -- is a fact about what was alarmed, so it
    /// is deliberately not settable here; a wrong crew size means the row was entered wrong and
    /// belongs to a correction path, not to routine status keeping.
    /// </summary>
    public ForceUnit WithStatusAndNotes(string? status, string? notes) => this with
    {
        Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
    };
}
