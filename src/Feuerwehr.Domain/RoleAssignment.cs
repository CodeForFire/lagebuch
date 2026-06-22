namespace Feuerwehr.Domain;

public sealed record RoleAssignment(
    Guid Id,
    string Role,
    string PersonName,
    string? CallSign,
    DateTimeOffset? From,
    DateTimeOffset? To)
{
    public static RoleAssignment Create(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Funktion darf nicht leer sein.", nameof(role));
        if (string.IsNullOrWhiteSpace(personName))
            throw new ArgumentException("Name darf nicht leer sein.", nameof(personName));

        return new RoleAssignment(
            Guid.NewGuid(),
            role.Trim(),
            personName.Trim(),
            string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim(),
            from,
            to);
    }
}
