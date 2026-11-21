using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Measurements;

/// <summary>
/// One Messgröße of the Messprotokoll (#930): a gas with its unit, direction and bands, copied by
/// value from the Stammdaten Messgase like the Gasmessprotokoll's <see cref="IncidentGas"/>, so
/// editing the Stammdaten later never changes how an old log reads. Optionally linked to the
/// <see cref="MeasuringDevice"/> that measures it. A Messgröße with readings is deactivated,
/// never removed.
/// </summary>
public sealed record MeasurementChannel(Guid Id, GasDefinition Definition, Guid? DeviceId, bool IsActive, string? Note)
{
    /// <summary>How many Messgrößen one Einsatz keeps. Separate from
    /// <see cref="GasDefinition.MaxCount"/>, which caps the Stammdaten list.</summary>
    public const int MaxCount = 10;

    public const int MaxNoteLength = 200;

    public string Name => Definition.Name;

    public string Unit => Definition.Unit;

    /// <summary>The same channel with its gas sanitised (<see cref="GasDefinition.Sanitized"/>)
    /// and its note trimmed and cut; null when the gas has no name.</summary>
    public MeasurementChannel? Sanitized() =>
        Definition.Sanitized() is { } definition
            ? new MeasurementChannel(Id, definition, DeviceId, IsActive, MeasurementText.Clean(Note, MaxNoteLength))
            : null;
}
