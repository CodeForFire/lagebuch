using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public sealed class LagekarteRoundTripTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lagebuch-test-").FullName;

    private string Path_ => Path.Join(_dir, "einsatz.lagebuch");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_Einsatzort_and_each_unit_s_position_survive_save_and_load()
    {
        var (incident, unitId) = PlacedIncident();

        IncidentRepository.Save(Path_, incident);
        var loaded = IncidentRepository.Load(Path_);

        Assert.Equal(new GeoPoint(48.1785, 11.2555), loaded.Location);
        Assert.Equal(new GeoPoint(48.17633, 11.25881), loaded.Forces.Single(u => u.Id == unitId).Position);
        Assert.Null(loaded.Forces.Single(u => u.Id != unitId).Position);
    }

    [Fact]
    public void An_Einsatz_never_put_on_the_map_loads_without_marks()
    {
        var clock = new FixedTestClock();
        var incident = Incident.Start(clock, new SessionOperator("Muster", "FFB 12/1"));
        incident.AddForceUnit(clock, new SessionOperator("Muster", "FFB 12/1"), "Aich", 6);

        IncidentRepository.Save(Path_, incident);
        var loaded = IncidentRepository.Load(Path_);

        Assert.Null(loaded.Location);
        Assert.Null(loaded.Forces[0].Position);
    }

    [Theory]
    [InlineData("'nord'", "11.2")]
    [InlineData("95.0", "11.2")]
    [InlineData("48.1", "NULL")]
    public void A_coordinate_a_hostile_file_stores_as_no_position_costs_the_mark_not_the_Einsatz(string lat, string lon)
    {
        var (incident, _) = PlacedIncident();
        IncidentRepository.Save(Path_, incident);
        Tamper($"UPDATE incident_meta SET location_lat = {lat}, location_lon = {lon};");
        Tamper($"UPDATE force_units SET position_lat = {lat}, position_lon = {lon};");

        var loaded = IncidentRepository.Load(Path_);

        Assert.Null(loaded.Location);
        Assert.All(loaded.Forces, u => Assert.Null(u.Position));
        Assert.Equal(2, loaded.Forces.Count);
    }

    private static (Incident Incident, Guid UnitId) PlacedIncident()
    {
        var clock = new FixedTestClock();
        var op = new SessionOperator("Muster", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        var unit = incident.AddForceUnit(clock, op, "Aich", 6, callSign: "LF 10/1");
        incident.AddForceUnit(clock, op, "Puch", 9, callSign: "LF 20/1");
        incident.SetLocation(clock, op, new GeoPoint(48.1785, 11.2555));
        incident.PlaceForceUnit(clock, op, unit.Id, new GeoPoint(48.17633, 11.25881));
        return (incident, unit.Id);
    }

    [SuppressMessage("Security", "CA2100", Justification = "Test-only: the statement is built from this test's own InlineData constants to play a hostile file.")]
    private void Tamper(string sql)
    {
        using var cn = SqliteConnectionFactory.OpenExisting(Path_);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private sealed class FixedTestClock : IClock
    {
        public DateTimeOffset Now => new(2026, 10, 27, 14, 0, 0, TimeSpan.FromHours(1));
    }
}
