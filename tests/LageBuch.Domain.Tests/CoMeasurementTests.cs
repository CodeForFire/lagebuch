using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Tests;

public class CoMeasurementTests
{
    [Fact]
    public void Building_Create_SetsProperties()
    {
        var building = Building.Create("Haus A", 8, 10, 0);
        Assert.Equal("Haus A", building.Name);
        Assert.Equal(8, building.FloorCount);
        Assert.Equal(10, building.ApartmentsPerFloor);
        Assert.Equal(0, building.Ordinal);
        Assert.NotEqual(Guid.Empty, building.Id);
    }

    [Fact]
    public void Building_Create_TrimsName()
    {
        var building = Building.Create("  Haus A  ", 8, 10, 0);
        Assert.Equal("Haus A", building.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public void Building_Create_InvalidFloorCount_Throws(int floorCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Building.Create("Haus A", floorCount, 10, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Building_Create_InvalidApartments_Throws(int apartments)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Building.Create("Haus A", 8, apartments, 0));
    }

    [Fact]
    public void Building_Create_EmptyName_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Building.Create(string.Empty, 8, 10, 0));
    }

    [Fact]
    public void Dwelling_Create_SetsProperties()
    {
        var buildingId = Guid.NewGuid();
        var dwelling = Dwelling.Create(buildingId, 2, 3);
        Assert.Equal(buildingId, dwelling.BuildingId);
        Assert.Equal(2, dwelling.FloorOrdinal);
        Assert.Equal(3, dwelling.ApartmentNumber);
        Assert.Equal(DwellingStatus.NotSearched, dwelling.Status);
        Assert.Null(dwelling.CoValue);
        Assert.Null(dwelling.ResidentName);
        Assert.Null(dwelling.KeyAvailable);
    }

    [Fact]
    public void CoMeasurementLabels_FloorLabel_EG()
    {
        Assert.Equal("EG", CoMeasurementLabels.FloorLabel(0));
    }

    [Fact]
    public void CoMeasurementLabels_FloorLabel_OG()
    {
        Assert.Equal("3. OG", CoMeasurementLabels.FloorLabel(3));
    }

    [Fact]
    public void CoMeasurementLabels_ApartmentLabel()
    {
        Assert.Equal("Whg. 5", CoMeasurementLabels.ApartmentLabel(5));
    }

    [Fact]
    public void CoMeasurementLabels_StatusText()
    {
        Assert.Equal("noch nicht abgesucht", CoMeasurementLabels.StatusText(DwellingStatus.NotSearched));
        Assert.Equal("abgesucht – keine Personen betroffen", CoMeasurementLabels.StatusText(DwellingStatus.Searched));
        Assert.Equal("Person(en) betroffen", CoMeasurementLabels.StatusText(DwellingStatus.Affected));
    }

    [Fact]
    public void CoMeasurementLabels_DwellingLocation()
    {
        var building = Building.Create("Haus A", 8, 10, 0);
        Assert.Equal(
            "Haus A, 3. OG, Whg. 2",
            CoMeasurementLabels.DwellingLocation(building, 3, 2));
    }

    [Fact]
    public void Building_WithStructure_UpdatesCounts()
    {
        var building = Building.Create("Haus A", 8, 10, 0);
        var updated = building.WithStructure(6, 8);
        Assert.Equal(6, updated.FloorCount);
        Assert.Equal(8, updated.ApartmentsPerFloor);
    }

    [Fact]
    public void Building_WithFloorDescription_SetsDescription()
    {
        var building = Building.Create("Haus A", 8, 10, 0);
        var updated = building.WithFloorDescription(3, "links");
        Assert.Equal("links", updated.FloorDescriptions[3]);
    }

    [Fact]
    public void Building_WithFloorDescription_EmptyRemoves()
    {
        var building = Building.Create("Haus A", 8, 10, 0)
            .WithFloorDescription(3, "links");
        var updated = building.WithFloorDescription(3, string.Empty);
        Assert.False(updated.FloorDescriptions.ContainsKey(3));
    }

    [Fact]
    public void Incident_AddCoBuilding_CreatesBuildingAndDwellings()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);

        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);

        Assert.Single(incident.Buildings);
        Assert.Equal("Haus A", incident.Buildings[0].Name);
        Assert.Equal(9, incident.Dwellings.Count);
        Assert.All(incident.Dwellings, d => Assert.Equal(DwellingStatus.NotSearched, d.Status));
    }

    [Fact]
    public void Incident_AddCoBuilding_LogsToETB()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);

        incident.AddCoBuilding(clock, op, "Haus A", 8, 10);

        var entry = incident.Journal.Last();
        Assert.Contains("CO-Messprotokoll eröffnet", entry.Text, StringComparison.Ordinal);
        Assert.Contains("Haus A", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Incident_RecordCoValue_OnlyLogsOnRealChange()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var journalCountBefore = incident.Journal.Count;

        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 45);
        Assert.Equal(journalCountBefore + 1, incident.Journal.Count);

        // Same value - no new entry
        incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, 45);
        Assert.Equal(journalCountBefore + 1, incident.Journal.Count);
    }

    [Fact]
    public void Incident_RecordCoValue_NegativeValue_Throws()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            incident.RecordCoValue(clock, op, incident.Buildings[0].Id, 0, 1, -1));
    }

    [Fact]
    public void Incident_SetDwellingStatus_OnlyLogsOnRealChange()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var journalCountBefore = incident.Journal.Count;

        incident.SetDwellingStatus(clock, op, incident.Buildings[0].Id, 0, 1, DwellingStatus.Searched);
        Assert.Equal(journalCountBefore + 1, incident.Journal.Count);

        // Same status - no new entry
        incident.SetDwellingStatus(clock, op, incident.Buildings[0].Id, 0, 1, DwellingStatus.Searched);
        Assert.Equal(journalCountBefore + 1, incident.Journal.Count);
    }

    [Fact]
    public void Incident_UpdateCoBuildingStructure_RemovesDwellings()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 4, 5); // 5 floors * 5 apts = 25

        incident.UpdateCoBuildingStructure(clock, op, incident.Buildings[0].Id, 2, 3);

        Assert.Equal(2, incident.Buildings[0].FloorCount);
        Assert.Equal(3, incident.Buildings[0].ApartmentsPerFloor);
        Assert.Equal(9, incident.Dwellings.Count); // 3 floors * 3 apts
    }

    [Fact]
    public void Incident_RemoveCoBuilding_RemovesAllDwellings()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);

        incident.RemoveCoBuilding(clock, op, incident.Buildings[0].Id);

        Assert.Empty(incident.Buildings);
        Assert.Empty(incident.Dwellings);
    }

    [Fact]
    public void Incident_EnsureOpen_ThrowsOnClosed()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() =>
            incident.AddCoBuilding(clock, op, "Haus A", 2, 3));
    }

    [Fact]
    public void Incident_SetDwellingDetails_DoesNotLogToETB()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var journalCountBefore = incident.Journal.Count;

        incident.SetDwellingDetails(incident.Buildings[0].Id, 0, 1, "Müller", true);

        Assert.Equal(journalCountBefore, incident.Journal.Count);
    }
}
