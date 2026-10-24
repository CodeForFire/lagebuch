namespace LageBuch.Domain.Equipment;

/// <summary>
/// One line of the Ausgabeliste: a Gerät the ELW handed out (a Mobilteil, a Weste, the AED), to
/// whom, and whether it has come back. The Gerät is the Stammdaten name copied at the moment of
/// issue, never a reference into the catalogue. Kept out of the ETB like a Beteiligte/r, because
/// the recipient's name and phone number are personal data. Immutable like every other aggregate
/// child: a correction or a return produces a replacement.
/// </summary>
public sealed record IssuedItem
{
    // Caps bound the storage and snapshot footprint of input that may come from a sync peer; the
    // domain enforces them, so the host's CommandApplier and the view model both reach them.
    public const int MaxDeviceLength = 200;

    public const int MaxIssuedToLength = 200;

    public const int MaxPhoneLength = 50;

    public const int MaxDetailsLength = 500;

    public const int MaxRemarkLength = 2000;

    private IssuedItem()
    {
    }

    public Guid Id { get; private init; }

    /// <summary>The Gerät as it was picked or typed: "Mobilteil 1", "Weste EAL".</summary>
    public string Device { get; private init; } = string.Empty;

    /// <summary>The person the Gerät was handed to.</summary>
    public string IssuedTo { get; private init; } = string.Empty;

    /// <summary>That person's phone number exactly as typed, or null.</summary>
    public string? IssuedToPhone { get; private init; }

    /// <summary>Who that person is beyond the name: "THW OV Beispielstadt", another way to reach them.</summary>
    public string? IssuedToDetails { get; private init; }

    /// <summary>A note about the Gerät itself, e.g. what "Sonstiges" was.</summary>
    public string? Remark { get; private init; }

    public DateTimeOffset IssuedAt { get; private init; }

    public string IssuedBy { get; private init; } = string.Empty;

    /// <summary>When the Gerät came back; null while it is still out.</summary>
    public DateTimeOffset? ReturnedAt { get; private init; }

    /// <summary>Who took it back; null exactly when <see cref="ReturnedAt"/> is.</summary>
    public string? ReturnedBy { get; private init; }

    public bool IsOut => ReturnedAt is null;

    public static IssuedItem Create(
        DateTimeOffset issuedAt,
        string device,
        string issuedTo,
        string? issuedToPhone,
        string? issuedToDetails,
        string? remark,
        SessionOperator @operator)
    {
        ArgumentNullException.ThrowIfNull(@operator);
        var (validDevice, validIssuedTo, validPhone, validDetails, validRemark) =
            Validate(device, issuedTo, issuedToPhone, issuedToDetails, remark);
        return new IssuedItem
        {
            Id = Guid.NewGuid(),
            Device = validDevice,
            IssuedTo = validIssuedTo,
            IssuedToPhone = validPhone,
            IssuedToDetails = validDetails,
            Remark = validRemark,
            IssuedAt = issuedAt,
            IssuedBy = @operator.Display,
        };
    }

    public static IssuedItem Rehydrate(
        Guid id,
        string device,
        string issuedTo,
        string? issuedToPhone,
        string? issuedToDetails,
        string? remark,
        DateTimeOffset issuedAt,
        string issuedBy,
        DateTimeOffset? returnedAt,
        string? returnedBy)
        => new()
        {
            Id = id,
            Device = device,
            IssuedTo = issuedTo,
            IssuedToPhone = issuedToPhone,
            IssuedToDetails = issuedToDetails,
            Remark = remark,
            IssuedAt = issuedAt,
            IssuedBy = issuedBy,
            ReturnedAt = returnedAt,
            ReturnedBy = returnedBy,
        };

    /// <summary>Returns a copy with corrected text, validated the same way <see cref="Create"/>
    /// does. Identity, the issue stamp and any return stay.</summary>
    public IssuedItem WithDetails(string device, string issuedTo, string? issuedToPhone, string? issuedToDetails, string? remark)
    {
        var (validDevice, validIssuedTo, validPhone, validDetails, validRemark) =
            Validate(device, issuedTo, issuedToPhone, issuedToDetails, remark);
        return this with
        {
            Device = validDevice,
            IssuedTo = validIssuedTo,
            IssuedToPhone = validPhone,
            IssuedToDetails = validDetails,
            Remark = validRemark,
        };
    }

    /// <summary>
    /// Returns a copy taken back at <paramref name="at"/> by <paramref name="by"/>. An item already
    /// back keeps its first return, so two synced Lagebuchführer pressing ZURÜCK agree on one time.
    /// </summary>
    public IssuedItem WithReturn(DateTimeOffset at, SessionOperator by)
    {
        ArgumentNullException.ThrowIfNull(by);
        return IsOut ? this with { ReturnedAt = at, ReturnedBy = by.Display } : this;
    }

    /// <summary>Returns a copy that is out again, for a ZURÜCK pressed on the wrong line.</summary>
    public IssuedItem WithoutReturn() =>
        IsOut ? this : this with { ReturnedAt = null, ReturnedBy = null };

    private static (string Device, string IssuedTo, string? Phone, string? Details, string? Remark) Validate(
        string device, string issuedTo, string? phone, string? details, string? remark)
    {
        var validDevice = Required(device, MaxDeviceLength, "Gerät", nameof(device));
        var validIssuedTo = Required(issuedTo, MaxIssuedToLength, "Ausgegeben an", nameof(issuedTo));
        var validPhone = Optional(phone, MaxPhoneLength, "Telefon", nameof(phone));
        var validDetails = Optional(details, MaxDetailsLength, "Organisation / Kontakt", nameof(details));
        var validRemark = Optional(remark, MaxRemarkLength, "Bemerkung", nameof(remark));
        return (validDevice, validIssuedTo, validPhone, validDetails, validRemark);
    }

    private static string Required(string value, int max, string label, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} darf nicht leer sein.", paramName);
        }

        return Optional(value, max, label, paramName) ?? string.Empty;
    }

    private static string? Optional(string? value, int max, string label, string paramName)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (trimmed is { Length: var length } && length > max)
        {
            throw new ArgumentException($"{label} ist länger als das Limit von {max} Zeichen.", paramName);
        }

        return trimmed;
    }
}
