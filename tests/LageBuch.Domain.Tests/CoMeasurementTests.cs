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
            Building.Create("", 8, 10, 0));
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
        Assert.Equal("Haus A, 3. OG, Whg. 2",
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
        var updated = building.WithFloorDescription(3, "");
        Assert.False(updated.FloorDescriptions.ContainsKey(3));
    }
}
