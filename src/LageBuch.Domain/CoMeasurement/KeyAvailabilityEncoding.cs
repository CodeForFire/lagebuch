namespace LageBuch.Domain.CoMeasurement;

/// <summary>The one mapping to and from the <c>bool?</c> that <c>co_dwellings.key_available</c>
/// (NULL/1/0) and the sync contract (<c>UpdateDwellingDetailsCommand</c>, <c>DwellingDto</c>)
/// carry. Kept that way so files and peers from before the enum read unchanged, with neither a
/// migration nor a protocol bump.</summary>
public static class KeyAvailabilityEncoding
{
    public static bool? ToNullableBool(this KeyAvailability value) => value switch
    {
        KeyAvailability.Available => true,
        KeyAvailability.NotAvailable => false,
        _ => null,
    };

    public static KeyAvailability FromNullableBool(bool? value) =>
        value is { } known ? (known ? KeyAvailability.Available : KeyAvailability.NotAvailable) : KeyAvailability.Unknown;
}
