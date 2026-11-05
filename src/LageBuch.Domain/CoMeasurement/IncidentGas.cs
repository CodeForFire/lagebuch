namespace LageBuch.Domain.CoMeasurement;

/// <summary>
/// A gas as an incident holds it (#410): its own copy of a <see cref="GasDefinition"/>, taken from
/// the Stammdaten when the Einsatz opened its first Haus, with an id that its readings point at.
/// The id belongs to the incident, never to the Stammdaten, so the file reads the same after the
/// Stammdaten are edited, on another Feuerwehr's machine, or years later.
/// </summary>
public sealed record IncidentGas(Guid Id, GasDefinition Definition)
{
    /// <summary>The id of the CO gas every file from before #410 gets on migration, so its
    /// <c>co_value</c> and Messreihe have a gas to belong to. Fixed, because the migration writes
    /// it in SQL and the domain falls back on it for a file whose gas table is missing.</summary>
    public static readonly Guid LegacyCoId = new("c0c0c0c0-0410-4c0c-8c0c-000000000001");

    public string Name => Definition.Name;

    public string Unit => Definition.Unit;

    public static IncidentGas LegacyCo { get; } = new(LegacyCoId, GasDefinition.DefaultCo);
}
