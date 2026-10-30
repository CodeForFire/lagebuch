using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class TacticalSignTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    [Fact]
    public void A_unit_recorded_with_a_vehicle_type_keeps_it_trimmed()
    {
        var (incident, clock, op) = NewIncident();

        var unit = incident.AddForceUnit(clock, op, "Aich", 9, callSign: "Florian Aich 40/1", vehicleType: " HLF ");

        Assert.Equal("HLF", unit.VehicleType);
    }

    [Fact]
    public void A_blank_vehicle_type_reads_as_none()
    {
        var (incident, clock, op) = NewIncident();

        var unit = incident.AddForceUnit(clock, op, "Aich", 9, vehicleType: "  ");

        Assert.Null(unit.VehicleType);
    }

    [Fact]
    public void An_overlong_vehicle_type_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(() =>
            incident.AddForceUnit(clock, op, "Aich", 9, vehicleType: new string('X', VehicleTypes.MaxLength + 1)));
    }

    [Fact]
    public void A_vehicle_type_of_exactly_the_cap_is_kept()
    {
        var (incident, clock, op) = NewIncident();
        var type = new string('X', VehicleTypes.MaxLength);

        Assert.Equal(type, incident.AddForceUnit(clock, op, "Aich", 9, vehicleType: type).VehicleType);
    }

    [Fact]
    public void Setting_the_vehicle_type_later_is_recorded_in_the_etb()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9, callSign: "Florian Aich 40/1");

        var updated = incident.SetForceUnitVehicleType(clock, op, unit.Id, "HLF");

        Assert.Equal("HLF", updated.VehicleType);
        Assert.Same(updated, incident.Forces[0]);
        Assert.Equal("Aich (Florian Aich 40/1): Fahrzeugtyp HLF", incident.Journal[^1].Text);
        Assert.Equal("Florian Aich 40/1", incident.Journal[^1].From);
    }

    [Fact]
    public void Clearing_the_vehicle_type_is_recorded_too()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9, vehicleType: "HLF");

        incident.SetForceUnitVehicleType(clock, op, unit.Id, null);

        Assert.Null(incident.Forces[0].VehicleType);
        Assert.Equal("Aich: Fahrzeugtyp entfernt", incident.Journal[^1].Text);
    }

    [Fact]
    public void Setting_the_same_vehicle_type_again_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9, vehicleType: "HLF");
        var count = incident.Journal.Count;

        var same = incident.SetForceUnitVehicleType(clock, op, unit.Id, " HLF ");

        Assert.Equal(count, incident.Journal.Count);
        Assert.Same(incident.Forces[0], same);
    }

    [Fact]
    public void Moving_a_label_keeps_the_position_and_writes_nothing_to_the_etb()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9);
        incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);
        var count = incident.Journal.Count;

        var moved = incident.MoveForceUnitLabel(clock, op, unit.Id, new LabelOffset(40, -60));

        Assert.Equal(new LabelOffset(40, -60), moved.LabelOffset);
        Assert.Same(moved, incident.Forces[0]);
        Assert.Equal(Marktplatz, moved.Position);
        Assert.Equal(count, incident.Journal.Count);
    }

    [Fact]
    public void Taking_a_unit_off_the_map_forgets_where_its_label_was()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9);
        incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);
        incident.MoveForceUnitLabel(clock, op, unit.Id, new LabelOffset(40, -60));

        var removed = incident.PlaceForceUnit(clock, op, unit.Id, null);

        Assert.Null(removed.LabelOffset);
    }

    [Fact]
    public void Moving_the_pin_keeps_the_label_offset()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9);
        incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);
        incident.MoveForceUnitLabel(clock, op, unit.Id, new LabelOffset(40, -60));

        var moved = incident.PlaceForceUnit(clock, op, unit.Id, new GeoPoint(48.17633, 11.25881));

        Assert.Equal(new LabelOffset(40, -60), moved.LabelOffset);
    }

    [Fact]
    public void A_closed_Einsatz_takes_no_vehicle_type_or_label_move()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 9);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.SetForceUnitVehicleType(clock, op, unit.Id, "HLF"));
        Assert.Throws<IncidentClosedException>(() => incident.MoveForceUnitLabel(clock, op, unit.Id, new LabelOffset(1, 1)));
    }

    [Fact]
    public void An_unknown_unit_takes_no_vehicle_type_or_label_move()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.SetForceUnitVehicleType(clock, op, Guid.NewGuid(), "HLF"));
        Assert.Throws<KeyNotFoundException>(() => incident.MoveForceUnitLabel(clock, op, Guid.NewGuid(), new LabelOffset(1, 1)));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(LabelOffset.MaxPixels + 1, 0)]
    [InlineData(0, -LabelOffset.MaxPixels - 1)]
    public void A_label_offset_off_any_screen_is_refused(double dx, double dy)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LabelOffset(dx, dy));
        Assert.Null(LabelOffset.TryCreate(dx, dy));
    }

    [Theory]
    [InlineData(LabelOffset.MaxPixels, -LabelOffset.MaxPixels)]
    [InlineData(0, 0)]
    public void A_label_offset_within_the_cap_is_kept(double dx, double dy) =>
        Assert.Equal(new LabelOffset(dx, dy), LabelOffset.TryCreate(dx, dy));

    [Fact]
    public void A_label_offset_with_either_half_missing_reads_as_none()
    {
        Assert.Null(LabelOffset.TryCreate(null, 4));
        Assert.Null(LabelOffset.TryCreate(4, null));
    }

    [Theory]
    [InlineData("HLF", "HLF")]
    [InlineData("hlf", "HLF")]
    [InlineData(" DLK ", "DLK")]
    [InlineData("ELW 1", "ELW 1")]
    public void A_known_vehicle_type_is_found_by_its_code(string value, string code) =>
        Assert.Equal(code, VehicleTypes.Find(value)?.Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Wechsellader-Sonderbau")]
    public void An_unknown_vehicle_type_is_not_found(string? value) =>
        Assert.Null(VehicleTypes.Find(value));

    [Fact]
    public void Every_catalogue_code_fits_the_cap_and_is_unique()
    {
        Assert.All(VehicleTypes.All, t => Assert.InRange(t.Code.Length, 1, VehicleTypes.MaxLength));
        Assert.Equal(VehicleTypes.All.Count, VehicleTypes.All.Select(t => t.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
