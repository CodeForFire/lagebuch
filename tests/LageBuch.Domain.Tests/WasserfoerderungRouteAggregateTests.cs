using LageBuch.Domain.Wasserfoerderung;

namespace LageBuch.Domain.Tests;

public class WasserfoerderungRouteAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly IReadOnlyList<GeoPoint> Route = new[]
    {
        new GeoPoint(48.000, 11.000),
        new GeoPoint(48.002, 11.000),
    };

    private static readonly IReadOnlyList<ElevationProfileSample> Profile = new[]
    {
        new ElevationProfileSample(0, 0),
        new ElevationProfileSample(200, 50),
        new ElevationProfileSample(400, 0),
    };

    private static (Incident Incident, FixedClock Clock) NewIncident()
    {
        var clock = new FixedClock(T0);
        return (Incident.Start(clock, new SessionOperator("Müller")), clock);
    }

    [Fact]
    public void AddLeitungFromRoute_stores_route_points_and_the_profile_derived_plan()
    {
        var (incident, _) = NewIncident();

        var leitung = incident.AddWasserfoerderungLeitungFromRoute("TLF 20/8", "FFB 1/44/1", Route, Profile);

        Assert.Equal(Route, leitung.RoutePoints);
        Assert.Equal(400, leitung.LengthMeters);
        Assert.Equal(0, leitung.ElevationRiseMeters); // net rise: profile starts and ends at 0 m
        Assert.Equal(1, leitung.PumpCount); // the interior crest forces a pump (see planner tests)
    }

    [Fact]
    public void AddWasserfoerderungLeitung_manual_entry_leaves_route_points_null()
    {
        var (incident, _) = NewIncident();

        var leitung = incident.AddWasserfoerderungLeitung(null, null, 2000, 0);

        Assert.Null(leitung.RoutePoints);
    }

    [Fact]
    public void Closed_incident_rejects_route_based_leitung()
    {
        var (incident, clock) = NewIncident();
        incident.Close(clock, new SessionOperator("Müller"));

        Assert.Throws<IncidentClosedException>(
            () => incident.AddWasserfoerderungLeitungFromRoute(null, null, Route, Profile));
    }

    [Fact]
    public void Rehydrate_round_trips_route_points()
    {
        var restored = WasserfoerderungLeitung.Rehydrate(
            id: Guid.NewGuid(),
            number: 1,
            uebergabestelle: null,
            ansprechpartner: null,
            flowLMin: 800,
            feedPressureBar: 8,
            lengthM: 400,
            elevationRiseM: 0,
            hoseCount: 20,
            reserveHoseCount: 4,
            pumpCount: 1,
            reservePumpCount: 1,
            pumpPositionsMeters: new[] { 0.0, 180 },
            routePoints: Route);

        Assert.Equal(Route, restored.RoutePoints);
    }

    [Fact]
    public void Rehydrate_defaults_route_points_to_null()
    {
        var restored = WasserfoerderungLeitung.Rehydrate(
            id: Guid.NewGuid(),
            number: 1,
            uebergabestelle: null,
            ansprechpartner: null,
            flowLMin: 800,
            feedPressureBar: 8,
            lengthM: 400,
            elevationRiseM: 0,
            hoseCount: 20,
            reserveHoseCount: 4,
            pumpCount: 0,
            reservePumpCount: 0,
            pumpPositionsMeters: new[] { 0.0 });

        Assert.Null(restored.RoutePoints);
    }
}
