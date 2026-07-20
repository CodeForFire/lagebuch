namespace Feuerwehr.Domain;

public sealed record RoleAssignment(
    Guid Id,
    string Role,
    string PersonName,
    string? CallSign,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Section,
    string? Phone)
{
    public static RoleAssignment Create(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Funktion darf nicht leer sein.", nameof(role));
        if (string.IsNullOrWhiteSpace(personName))
            throw new ArgumentException("Name darf nicht leer sein.", nameof(personName));
        if (from is { } f && to is { } t && t < f)
            throw new ArgumentException("Bis-Zeitpunkt darf nicht vor dem Von-Zeitpunkt liegen.", nameof(to));

        return new RoleAssignment(
            Guid.NewGuid(),
            role.Trim(),
            personName.Trim(),
            Trimmed(callSign),
            from,
            to,
            Trimmed(section),
            Trimmed(phone));
    }

    /// <summary>
    /// Returns a copy that ends at <paramref name="to"/>. Assignments are immutable records held
    /// in a list on the aggregate, so ending one means replacing it rather than mutating it.
    /// </summary>
    public RoleAssignment EndedAt(DateTimeOffset to)
    {
        if (From is { } f && to < f)
            throw new ArgumentException("Bis-Zeitpunkt darf nicht vor dem Von-Zeitpunkt liegen.", nameof(to));
        return this with { To = to };
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
