namespace LageBuch.Domain.Measurements;

/// <summary>A Messgerät in use at this Einsatz (#930): what it is, its Gerätenummer, which
/// Fahrzeug it came on and when it was last checked (Begasung, Nullpunkt).</summary>
public sealed record MeasuringDevice(Guid Id, DeviceKind Kind, string Name, string? Number, string? Vehicle, string? Check)
{
    public const int MaxCount = 20;

    public const int MaxNameLength = 40;

    public const int MaxNumberLength = 60;

    public const int MaxVehicleLength = 60;

    public const int MaxCheckLength = 60;

    /// <summary>The same device with text trimmed and cut and an unknown kind read as
    /// <see cref="DeviceKind.Other"/>; null when it has no name.</summary>
    public MeasuringDevice? Sanitized() =>
        MeasurementText.Clean(Name, MaxNameLength) is { } name
            ? new MeasuringDevice(
                Id,
                Enum.IsDefined(Kind) ? Kind : DeviceKind.Other,
                name,
                MeasurementText.Clean(Number, MaxNumberLength),
                MeasurementText.Clean(Vehicle, MaxVehicleLength),
                MeasurementText.Clean(Check, MaxCheckLength))
            : null;
}
