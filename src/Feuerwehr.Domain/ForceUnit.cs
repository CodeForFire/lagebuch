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
}
