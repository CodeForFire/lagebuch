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

    /// <summary>The CO-Messung's Wohnung this person was found in (#702), or null.</summary>
    public Guid? DwellingId { get; private init; }

    /// <summary>
    /// Where that Wohnung was when the link was made, e.g. "Hauptstr. 12, 1. OG, Whg. 2": a copy,
    /// so the entry still says where once the Wohnung has left the building.
    /// </summary>
    public string? DwellingLabel { get; private init; }

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
        Guid id,
        string name,
        string? phone,
        string? notes,
        string createdBy,
        DateTimeOffset createdAt,
        Guid? dwellingId = null,
        string? dwellingLabel = null)
        => new()
        {
            Id = id,
            Name = name,
            Phone = phone,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = createdAt,
            DwellingId = dwellingId,
            DwellingLabel = dwellingLabel,
        };

    /// <summary>Returns a copy with corrected details, validated and normalised the same way
    /// <see cref="Create"/> does. Id and creation stamp stay.</summary>
    public InvolvedParty WithDetails(string name, string? phone, string? notes)
    {
        var (validName, validPhone, validNotes) = Validate(name, phone, notes);
        return this with { Name = validName, Phone = validPhone, Notes = validNotes };
    }

    /// <summary>Returns a copy found in <paramref name="dwellingId"/>, described as <paramref name="label"/>; both null clears it.</summary>
    public InvolvedParty WithDwelling(Guid? dwellingId, string? label) =>
        this with { DwellingId = dwellingId, DwellingLabel = label };

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
