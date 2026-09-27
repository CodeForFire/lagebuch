namespace LageBuch.Domain.Involved;

/// <summary>
/// A person involved in the Einsatz who is not part of the forces — the house owner, the vehicle
/// owner, the police contact. The Einsatz's own address book: a name, a phone number and a free
/// note, nothing else. Deliberately kept out of the ETB, so this personal data is only where the
/// Lagebuchführer put it. Immutable like every other aggregate child: a correction produces a
/// replacement via <see cref="WithDetails"/>.
/// </summary>
public sealed record InvolvedParty
{
    // Caps bound the storage and snapshot footprint of input that may come from a sync peer; the
    // domain enforces them, so the host's CommandApplier and the view model both reach them.
    public const int MaxNameLength = 200;

    public const int MaxPhoneLength = 50;

    public const int MaxNotesLength = 2000;

    private InvolvedParty()
    {
    }

    public Guid Id { get; private init; }

    public string Name { get; private init; } = string.Empty;

    public string? Phone { get; private init; }

    public string? Notes { get; private init; }

    public string CreatedBy { get; private init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private init; }

    public static InvolvedParty Create(
        DateTimeOffset createdAt, string name, string? phone, string? notes, SessionOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        var (validName, validPhone, validNotes) = Validate(name, phone, notes);
        return new InvolvedParty
        {
            Id = Guid.NewGuid(),
            Name = validName,
            Phone = validPhone,
            Notes = validNotes,
            CreatedBy = @operator.Display,
            CreatedAt = createdAt,
        };
    }

    public static InvolvedParty Rehydrate(
        Guid id, string name, string? phone, string? notes, string createdBy, DateTimeOffset createdAt)
        => new()
        {
            Id = id,
            Name = name,
            Phone = phone,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
        };

    /// <summary>Returns a copy with corrected details, validated and normalised the same way
    /// <see cref="Create"/> does. Id and creation stamp stay.</summary>
    public InvolvedParty WithDetails(string name, string? phone, string? notes)
    {
        var (validName, validPhone, validNotes) = Validate(name, phone, notes);
        return this with { Name = validName, Phone = validPhone, Notes = validNotes };
    }

    private static (string Name, string? Phone, string? Notes) Validate(string name, string? phone, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name darf nicht leer sein.", nameof(name));
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
        {
            throw new ArgumentException($"Name ist länger als das Limit von {MaxNameLength} Zeichen.", nameof(name));
        }

        var trimmedPhone = Trimmed(phone);
        if (trimmedPhone is { Length: > MaxPhoneLength })
        {
            throw new ArgumentException($"Telefon ist länger als das Limit von {MaxPhoneLength} Zeichen.", nameof(phone));
        }

        var trimmedNotes = Trimmed(notes);
        if (trimmedNotes is { Length: > MaxNotesLength })
        {
            throw new ArgumentException($"Notiz ist länger als das Limit von {MaxNotesLength} Zeichen.", nameof(notes));
        }

        return (trimmedName, trimmedPhone, trimmedNotes);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
