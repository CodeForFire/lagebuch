namespace Feuerwehr.Domain;

public sealed record ForceUnit(
    Guid Id,
    string Brigade,
    string? CallSign,
    int PersonnelCount,
    string? Status,
    string? Notes)
{
    public static ForceUnit Create(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(brigade))
            throw new ArgumentException("Feuerwehr darf nicht leer sein.", nameof(brigade));
        if (personnelCount < 0)
            throw new ArgumentOutOfRangeException(nameof(personnelCount));

        return new ForceUnit(
            Guid.NewGuid(),
            brigade.Trim(),
            string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            personnelCount,
            string.IsNullOrWhiteSpace(status) ? null : status.Trim(),
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
    }
}
