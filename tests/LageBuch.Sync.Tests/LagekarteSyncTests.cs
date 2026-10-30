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

    [Fact]
    public void A_unit_recorded_on_a_client_with_a_vehicle_type_carries_it_on_the_host()
    {
        var (incident, clock) = NewIncident();

        ApplyOverWire(new AddForceUnitCommand(Peer, "Aich", 9, "Florian Aich 40/1", null, null, 0, VehicleType: "HLF"), incident, clock);

        Assert.Equal("HLF", incident.Forces.Single().VehicleType);
    }

    [Fact]
    public void A_vehicle_type_set_on_a_client_reaches_the_host_and_is_attributed()
    {
        var (incident, clock) = NewIncident();
        var unit = incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 9);

        ApplyOverWire(new SetForceUnitVehicleTypeCommand(Peer, unit.Id, "DLK"), incident, clock);

        Assert.Equal("DLK", incident.Forces.Single().VehicleType);
        Assert.Equal("Bauer (FFB 3)", incident.Journal[^1].EnteredBy);
    }

    [Fact]
    public void An_overlong_vehicle_type_from_a_peer_is_refused_and_changes_nothing()
    {
        var (incident, clock) = NewIncident();
        var unit = incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 9, vehicleType: "LF");
        var entries = incident.Journal.Count;
        var overlong = new string('X', VehicleTypes.MaxLength + 1);

        Assert.Throws<ArgumentException>(() => CommandApplier.Apply(new SetForceUnitVehicleTypeCommand(Peer, unit.Id, overlong), incident, clock));
        Assert.Throws<ArgumentException>(() => CommandApplier.Apply(new AddForceUnitCommand(Peer, "Puch", 9, null, null, null, 0, VehicleType: overlong), incident, clock));

        Assert.Equal("LF", incident.Forces.Single().VehicleType);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void A_label_moved_on_a_client_stands_there_on_the_host()
    {
        var (incident, clock) = NewIncident();
        var unit = incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 9);

        ApplyOverWire(new MoveForceUnitLabelCommand(Peer, unit.Id, 30, -50), incident, clock);
        Assert.Equal(new LabelOffset(30, -50), incident.Forces.Single().LabelOffset);

        ApplyOverWire(new MoveForceUnitLabelCommand(Peer, unit.Id, null, null), incident, clock);
        Assert.Null(incident.Forces.Single().LabelOffset);
    }

    [Theory]
    [InlineData(30.0, null)]
    [InlineData(null, -50.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NegativeInfinity)]
    [InlineData(1e9, 0.0)]
    public void A_label_offset_off_any_screen_is_refused_and_changes_nothing(double? dx, double? dy)
    {
        var (incident, clock) = NewIncident();
        var unit = incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 9);
        incident.MoveForceUnitLabel(clock, new SessionOperator("Host", "FFB 1"), unit.Id, new LabelOffset(10, 10));

        Assert.Throws<ArgumentException>(() => CommandApplier.Apply(new MoveForceUnitLabelCommand(Peer, unit.Id, dx, dy), incident, clock));

        Assert.Equal(new LabelOffset(10, 10), incident.Forces.Single().LabelOffset);
    }

    [Fact]
    public void A_joining_client_sees_each_unit_s_vehicle_type_and_label_offset()
    {
        var (incident, clock) = NewIncident();
        var op = new SessionOperator("Host", "FFB 1");
        var unit = incident.AddForceUnit(clock, op, "Aich", 9, vehicleType: "HLF");
        incident.MoveForceUnitLabel(clock, op, unit.Id, new LabelOffset(30, -50));

        var json = SyncJson.Serialize(SnapshotMapper.ToSnapshot(incident));
        var copy = SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(json)).Forces.Single();

        Assert.Equal("HLF", copy.VehicleType);
        Assert.Equal(new LabelOffset(30, -50), copy.LabelOffset);
    }

    [Fact]
    public void A_snapshot_from_a_hostile_host_with_bad_sign_details_costs_only_those()
    {
        var (incident, clock) = NewIncident();
        incident.AddForceUnit(clock, new SessionOperator("Host", "FFB 1"), "Aich", 9);
        var snapshot = SnapshotMapper.ToSnapshot(incident);
        snapshot = snapshot with { Forces = [snapshot.Forces[0] with { VehicleType = new string('X', 500), LabelDx = double.NaN, LabelDy = 4 }] };

        var copy = SnapshotMapper.FromSnapshot(snapshot).Forces.Single();

        Assert.Null(copy.VehicleType);
        Assert.Null(copy.LabelOffset);
        Assert.Equal("Aich", copy.Brigade);
    }

    private static (Incident Incident, FixedClock Clock) NewIncident()
    {
        var clock = new FixedClock();
        return (Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null), clock);
    }

    private static void ApplyOverWire(SyncCommand command, Incident incident, IClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);
}
