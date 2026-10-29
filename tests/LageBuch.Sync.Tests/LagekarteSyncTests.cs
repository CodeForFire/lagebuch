using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Sync.Tests;

public class LagekarteSyncTests
{
    private static readonly OperatorDto Peer = new("Bauer", "FFB 3");

    [Fact]
    public void The_Einsatzort_marked_on_a_client_reaches_the_host_and_is_attributed()
    {
        var (incident, clock) = NewIncident();

        ApplyOverWire(new SetIncidentLocationCommand(Peer, 48.1785, 11.2555), incident, clock);

        Assert.Equal(new GeoPoint(48.1785, 11.2555), incident.Location);
        Assert.Equal("Bauer (FFB 3)", incident.Journal[^1].EnteredBy);

        ApplyOverWire(new SetIncidentLocationCommand(Peer, null, null), incident, clock);
        Assert.Null(incident.Location);
    }

    [Fact]
    public void A_unit_placed_on_a_client_stands_there_on_the_host()
    {
        var (incident, clock) = NewIncident();
        var unit = incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 6, callSign: "LF 10/1");

        ApplyOverWire(new PlaceForceUnitCommand(Peer, unit.Id, 48.17633, 11.25881), incident, clock);
        Assert.Equal(new GeoPoint(48.17633, 11.25881), incident.Forces.Single().Position);

        ApplyOverWire(new PlaceForceUnitCommand(Peer, unit.Id, null, null), incident, clock);
        Assert.Null(incident.Forces.Single().Position);
    }

    // A peer's numbers are refused like any other bad command -- never read as "take it off".
    [Theory]
    [InlineData(48.1, null)]
    [InlineData(null, 11.2)]
    [InlineData(double.NaN, 11.2)]
    [InlineData(48.1, double.PositiveInfinity)]
    [InlineData(95.0, 11.2)]
    [InlineData(48.1, -999.0)]
    public void A_coordinate_that_is_no_position_is_refused_and_changes_nothing(double? lat, double? lon)
    {
        var (incident, clock) = NewIncident();
        var unit = incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 6);
        incident.SetLocation(clock, new SessionOperator("Host", "FFB 1"), new GeoPoint(48.1785, 11.2555));
        incident.PlaceForceUnit(clock, new SessionOperator("Host", "FFB 1"), unit.Id, new GeoPoint(48.1785, 11.2555));
        var entries = incident.Journal.Count;

        Assert.Throws<ArgumentException>(() => CommandApplier.Apply(new SetIncidentLocationCommand(Peer, lat, lon), incident, clock));
        Assert.Throws<ArgumentException>(() => CommandApplier.Apply(new PlaceForceUnitCommand(Peer, unit.Id, lat, lon), incident, clock));

        Assert.Equal(new GeoPoint(48.1785, 11.2555), incident.Location);
        Assert.Equal(new GeoPoint(48.1785, 11.2555), incident.Forces.Single().Position);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void A_joining_client_sees_the_Einsatzort_and_every_position()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Host", "FFB 1");
        var placed = incident.AddForceUnit(clock, op, "Aich", 6);
        incident.AddForceUnit(clock, op, "Puch", 9);
        incident.SetLocation(clock, op, new GeoPoint(48.1785, 11.2555));
        incident.PlaceForceUnit(clock, op, placed.Id, new GeoPoint(48.17633, 11.25881));

        var json = SyncJson.Serialize(SnapshotMapper.ToSnapshot(incident));
        var copy = SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(json));

        Assert.Equal(incident.Location, copy.Location);
        Assert.Equal(new GeoPoint(48.17633, 11.25881), copy.Forces.Single(u => u.Id == placed.Id).Position);
        Assert.Null(copy.Forces.Single(u => u.Id != placed.Id).Position);
    }

    [Fact]
    public void A_snapshot_from_a_hostile_host_with_impossible_coordinates_costs_the_marks_only()
    {
        var (incident, clock) = NewIncident();
        incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 6);
        var snapshot = SnapshotMapper.ToSnapshot(incident) with { LocationLatitude = double.NaN, LocationLongitude = 11.2 };
        snapshot = snapshot with { Forces = [snapshot.Forces[0] with { PositionLatitude = 400, PositionLongitude = 11.2 }] };

        var copy = SnapshotMapper.FromSnapshot(snapshot);

        Assert.Null(copy.Location);
        Assert.Null(copy.Forces.Single().Position);
    }

    private static (Incident Incident, FixedClock Clock) NewIncident()
    {
        var clock = new FixedClock();
        return (Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null), clock);
    }

    private static void ApplyOverWire(SyncCommand command, Incident incident, IClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);
}
