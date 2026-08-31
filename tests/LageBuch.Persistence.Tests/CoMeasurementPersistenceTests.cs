using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Time;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class CoMeasurementPersistenceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"co-{Guid.NewGuid():N}.fwincident");

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
