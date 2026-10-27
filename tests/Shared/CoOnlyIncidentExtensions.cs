using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;

namespace LageBuch;

/// <summary>
/// The single-gas shorthand most CO tests were written in before #410. An Einsatz whose first Haus
/// was opened without Stammdaten gases measures exactly one gas, CO, so "the CO value" is that
/// gas's value. Test-only: the product records gases by id, through
/// <see cref="Incident.RecordCoValues"/>. Linked into every test project that needs it.
/// </summary>
internal static class CoOnlyIncidentExtensions
{
    public static void RecordCoValue(this Incident incident, IClock clock, SessionOperator op, Guid buildingId, int floorOrdinal, int apartmentNumber, decimal? value) =>
        incident.RecordCoValues(clock, op, buildingId, floorOrdinal, apartmentNumber, new Dictionary<Guid, decimal?> { [incident.Gases[0].Id] = value });

    /// <summary>The value of the only gas measured here, or null when none is.</summary>
    public static decimal? CoValue(this Dwelling dwelling) => dwelling.Values.Count == 0 ? null : dwelling.Values.Values.Single();
}
