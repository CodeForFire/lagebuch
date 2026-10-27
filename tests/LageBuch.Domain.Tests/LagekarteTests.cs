using LageBuch.Domain.Etb;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class LagekarteTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 14, 0, 0, TimeSpan.FromHours(1));
    private static readonly GeoPoint Marktplatz = new(48.1785, 11.2555);
    private static readonly GeoPoint Amperbruecke = new(48.17633, 11.25881);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static EtbEntry Last(Incident incident) => incident.Journal[^1];

    [Fact]
    public void Marking_the_Einsatzort_records_it_and_its_coordinates_in_the_etb()
    {
        var (incident, clock, op) = NewIncident();

        incident.SetLocation(clock, op, Marktplatz);

        Assert.Equal(Marktplatz, incident.Location);
        Assert.Equal("Einsatzort auf der Lagekarte gesetzt (48,17850° N, 11,25550° O)", Last(incident).Text);
        Assert.Equal(EtbDirection.System, Last(incident).Direction);
    }

    [Fact]
    public void Moving_and_removing_the_Einsatzort_are_recorded_too()
    {
        var (incident, clock, op) = NewIncident();
        incident.SetLocation(clock, op, Marktplatz);

        incident.SetLocation(clock, op, Amperbruecke);
        Assert.Equal("Einsatzort auf der Lagekarte verlegt (48,17633° N, 11,25881° O)", Last(incident).Text);

        incident.SetLocation(clock, op, null);
        Assert.Null(incident.Location);
        Assert.Equal("Einsatzort von der Lagekarte genommen", Last(incident).Text);
    }

    [Fact]
    public void Marking_the_same_spot_again_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        incident.SetLocation(clock, op, Marktplatz);
        var count = incident.Journal.Count;

        incident.SetLocation(clock, op, new GeoPoint(48.1785, 11.2555));

        Assert.Equal(count, incident.Journal.Count);
    }

    [Fact]
    public void A_unit_put_on_the_map_is_recorded_from_its_call_sign()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");

        var placed = incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);

        Assert.Equal(Marktplatz, placed.Position);
        Assert.Same(placed, incident.Forces[0]);
        Assert.Equal("Aich (LF 10/1) auf der Lagekarte eingetragen", Last(incident).Text);
        Assert.Equal("LF 10/1", Last(incident).From);
    }

    [Fact]
    public void Moving_a_unit_already_on_the_map_writes_nothing_to_the_etb()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");
        incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);
        var count = incident.Journal.Count;

        var moved = incident.PlaceForceUnit(clock, op, unit.Id, Amperbruecke);

        Assert.Equal(Amperbruecke, moved.Position);
        Assert.Equal(count, incident.Journal.Count);
    }

    [Fact]
    public void Taking_a_unit_off_the_map_is_recorded()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");
        incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);

        var removed = incident.PlaceForceUnit(clock, op, unit.Id, null);

        Assert.Null(removed.Position);
        Assert.Equal("Aich (LF 10/1) von der Lagekarte genommen", Last(incident).Text);
    }

    [Fact]
    public void Placing_an_unknown_unit_is_refused() =>
        Assert.Throws<KeyNotFoundException>(() =>
        {
            var (incident, clock, op) = NewIncident();
            incident.PlaceForceUnit(clock, op, Guid.NewGuid(), Marktplatz);
        });

    [Fact]
    public void A_closed_Einsatz_takes_no_marks()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.SetLocation(clock, op, Marktplatz));
        Assert.Throws<IncidentClosedException>(() => incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz));
    }

    [Fact]
    public void A_position_survives_every_other_edit_of_the_unit()
    {
        var (incident, clock, op) = NewIncident();
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");
        incident.PlaceForceUnit(clock, op, unit.Id, Marktplatz);

        incident.UpdateForceUnit(clock, op, unit.Id, "Im Einsatz", "Nordseite");

        Assert.Equal(Marktplatz, incident.Forces[0].Position);
    }

    [Theory]
    [InlineData(48.1785, 11.2555, "48,17850° N, 11,25550° O")]
    [InlineData(-33.8568, 151.2153, "33,85680° S, 151,21530° O")]
    [InlineData(40.6892, -74.0445, "40,68920° N, 74,04450° W")]
    public void Coordinates_read_in_german_with_the_hemisphere_as_a_letter(double lat, double lon, string expected) =>
        Assert.Equal(expected, Formatting.Coordinates(new GeoPoint(lat, lon)));

    [Theory]
    [InlineData(null, 11.0)]
    [InlineData(48.0, null)]
    [InlineData(double.NaN, 11.0)]
    [InlineData(91.0, 11.0)]
    [InlineData(48.0, 181.0)]
    public void A_coordinate_pair_that_is_no_position_reads_as_none(double? lat, double? lon) =>
        Assert.Null(GeoPoint.TryCreate(lat, lon));
}
