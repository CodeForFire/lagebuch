using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class CoMeasurementPersistenceTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"co-{Guid.NewGuid():N}.fwincident");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static Incident CreateIncidentWithBuilding()
    {
        var clock = new Clock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 45);
        incident.SetDwellingStatus(clock, op, incident.Buildings[0].Id, 0, 2, DwellingStatus.Searched);
        incident.SetDwellingDetails(incident.Buildings[0].Id, 0, 1, "Müller", true);
        incident.SetFloorDescription(incident.Buildings[0].Id, 1, "rechts");
        return incident;
    }

    // --- Issue #424: the Messreihe ---
    [Fact]
    public void SaveLoad_RoundTrips_TheReadingSeries()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;

        incident.RecordCoValue(clock, op, haus, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(27);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);
        clock.Now = clock.Now.AddMinutes(21);
        incident.RecordCoValue(clock, op, haus, 0, 1, null);

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        var dwelling = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(new int?[] { 120, 40, null }, dwelling.Readings.Select(r => r.Value));
        Assert.All(dwelling.Readings, r => Assert.Equal("Huber (FFB 12/1)", r.RecordedBy));
        Assert.Equal(
            new[] { "10:00", "10:27", "10:48" },
            dwelling.Readings.Select(r => Formatting.TimeOfDay(r.MeasuredAt.ToUniversalTime())));
        Assert.Null(dwelling.CoValue);
    }

    // The ApplyV21 lesson: force_unit_edits needed a cleanup migration because Save's DELETE list
    // did not list it, so every save stacked another copy of the history onto the file. This is
    // the test that keeps co_readings from repeating it.
    [Fact]
    public void Save_Twice_DoesNotDuplicateTheSeries()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValue(clock, op, haus, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(27);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);

        IncidentRepository.Save(_path, incident);
        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);
        var dwelling = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(new int?[] { 120, 40 }, dwelling.Readings.Select(r => r.Value));
    }

    [Fact]
    public void SaveLoad_KeepsEachDwellingsSeriesApart()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var haus = incident.Buildings[0].Id;
        incident.RecordCoValue(clock, op, haus, 0, 1, 120);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordCoValue(clock, op, haus, 0, 1, 40);
        incident.RecordCoValue(clock, op, haus, 1, 2, 8);

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        Assert.Equal(2, loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Readings.Count);
        Assert.Single(loaded.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2).Readings);
        Assert.Empty(loaded.Dwellings.Single(d => d.FloorOrdinal == 2 && d.ApartmentNumber == 1).Readings);
    }

    // #419: removing a Wohnung renumbers the survivors. That has to move the records rather than
    // rebuild them — co_readings rows hang off Dwelling.Id, so a survivor recreated under a new id
    // would come back from disk with an empty Messreihe.
    [Fact]
    public void SaveLoad_AfterRemovingAWohnung_KeepsTheSurvivorsMessreihen()
    {
        var clock = new Clock();
        var op = new SessionOperator("Huber", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 4);
        var haus = incident.Buildings[0].Id;
        incident.SetApartmentLabel(haus, 0, 4, "Kiosk");
        incident.RecordCoValue(clock, op, haus, 0, 4, 120);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordCoValue(clock, op, haus, 0, 4, 40);

        incident.RemoveDwellings(clock, op, haus, 0, new[] { 2 });

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        // Whg. 4 is Whg. 3 now, with both readings and its Bezeichnung still attached.
        var moved = loaded.Dwellings.Single(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 3);
        Assert.Equal(40, moved.CoValue);
        Assert.Equal(2, moved.Readings.Count);
        Assert.Equal("Kiosk", CoMeasurementLabels.ApartmentLabel(loaded.Buildings[0], 0, 3));
        Assert.Equal(3, loaded.Buildings[0].ApartmentsFor(0));
        Assert.Equal(3, loaded.Dwellings.Count(d => d.FloorOrdinal == 0));
    }

    [Fact]
    public void SaveLoad_RoundTrip_BuildingsAndDwellings()
    {
        var original = CreateIncidentWithBuilding();

        IncidentRepository.Save(_path, original);

        var loaded = IncidentRepository.Load(_path);

        Assert.Single(loaded.Buildings);
        Assert.Equal("Haus A", loaded.Buildings[0].Name);
        Assert.Equal(2, loaded.Buildings[0].FloorCount);
        Assert.Equal(3, loaded.Buildings[0].ApartmentsPerFloor);
        Assert.Equal(9, loaded.Dwellings.Count);

        var dwelling = loaded.Dwellings.First(d =>
            d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(45, dwelling.CoValue);
        Assert.Equal("Müller", dwelling.ResidentName);
        Assert.True(dwelling.KeyAvailable);

        var searched = loaded.Dwellings.First(d =>
            d.FloorOrdinal == 0 && d.ApartmentNumber == 2);
        Assert.Equal(DwellingStatus.Searched, searched.Status);

        Assert.Equal("rechts", loaded.Buildings[0].FloorDescriptions[1]);
    }

    [Fact]
    public void SaveLoad_RoundTrip_NullableFields()
    {
        var clock = new Clock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 1);

        IncidentRepository.Save(_path, incident);

        var loaded = IncidentRepository.Load(_path);

        var dwelling = loaded.Dwellings[0];
        Assert.Null(dwelling.CoValue);
        Assert.Null(dwelling.ResidentName);
        Assert.Null(dwelling.KeyAvailable);
    }
}
