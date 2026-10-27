using LageBuch.Sync;

namespace LageBuch;

/// <summary>The session-level twin of <c>CoOnlyIncidentExtensions</c>: records a value for the
/// Einsatz's first gas, which in a test without Stammdaten gases is CO. Test-only.</summary>
internal static class CoOnlySessionExtensions
{
    public static void RecordCoValue(this IIncidentSession session, Guid buildingId, int floorOrdinal, int apartmentNumber, decimal? value) =>
        session.RecordCoValues(buildingId, floorOrdinal, apartmentNumber, new Dictionary<Guid, decimal?> { [session.Incident.Gases[0].Id] = value });
}
