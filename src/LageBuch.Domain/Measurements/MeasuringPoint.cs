namespace LageBuch.Domain.Measurements;

/// <summary>A place the Messtrupp measures at again and again (#930): "Kellerabgang",
/// "MP 3 Windrichtung 200 m". Not tied to a Haus, unlike the Gasmessprotokoll.</summary>
public sealed record MeasuringPoint(Guid Id, string Name, string? Location, MeasurementZone Zone)
{
    public const int MaxCount = 50;

    public const int MaxNameLength = 40;

    public const int MaxLocationLength = 120;

    /// <summary>The same point with text trimmed and cut and an unknown zone read as
    /// <see cref="MeasurementZone.Unknown"/>; null when it has no name. Used on everything a file
    /// or a sync peer hands over.</summary>
    public MeasuringPoint? Sanitized() =>
        MeasurementText.Clean(Name, MaxNameLength) is { } name
            ? new MeasuringPoint(
                Id,
                name,
                MeasurementText.Clean(Location, MaxLocationLength),
                Enum.IsDefined(Zone) ? Zone : MeasurementZone.Unknown)
            : null;
}
