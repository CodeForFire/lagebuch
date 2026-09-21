using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Tests;

/// <summary>#419: shrinking a floor used to delete Wohnungen off the right-hand end without
/// asking, and adding a Geschoss used to delete every unit above the building default. These
/// cover the domain half of both — what survives, what it keeps, and what the ETB says went.
/// </summary>
public class CoDwellingRemovalTests
{
    private static readonly DateTimeOffset At = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op, Guid BuildingId) Scene(
        int floorCount = 2, int apartmentsPerFloor = 3)
    {
        var clock = new FixedClock(At);
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", floorCount, apartmentsPerFloor);
        return (incident, clock, op, incident.Buildings[0].Id);
    }

    private static Dwelling Unit(Incident incident, Guid buildingId, int floor, int apartment) =>
        incident.Dwellings.Single(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == floor && d.ApartmentNumber == apartment);

    // --- Dwelling.HasData -------------------------------------------------------------------
    [Fact]
    public void HasData_IsFalse_ForAFreshWohnung()
    {
        var (incident, _, _, buildingId) = Scene();
        Assert.False(Unit(incident, buildingId, 0, 1).HasData);
    }

    [Fact]
    public void HasData_IsTrue_ForEachThingACrewCanRecord()
    {
        var (incident, clock, op, buildingId) = Scene();

        incident.SetDwellingDetails(buildingId, 0, 1, "Müller", null);
        Assert.True(Unit(incident, buildingId, 0, 1).HasData);

        incident.SetDwellingStatus(clock, op, buildingId, 0, 2, DwellingStatus.Searched);
        Assert.True(Unit(incident, buildingId, 0, 2).HasData);

        incident.SetDwellingDetails(buildingId, 0, 3, null, true);
        Assert.True(Unit(incident, buildingId, 0, 3).HasData);

        incident.RecordCoValue(clock, op, buildingId, 1, 1, 120);
        Assert.True(Unit(incident, buildingId, 1, 1).HasData);
    }

    // The case a CoValue-only check misses: clearing a measurement is recorded as a reading, so
    // the value is null again while the Messreihe still holds what was measured.
    [Fact]
    public void HasData_IsTrue_ForAClearedMeasurement_WhoseMessreiheRemains()
    {
        var (incident, clock, op, buildingId) = Scene();

        incident.RecordCoValue(clock, op, buildingId, 0, 1, 120);
        incident.RecordCoValue(clock, op, buildingId, 0, 1, null);

        var unit = Unit(incident, buildingId, 0, 1);
        Assert.Null(unit.CoValue);
        Assert.NotEmpty(unit.Readings);
        Assert.True(unit.HasData);
    }

    // --- RemoveDwellings --------------------------------------------------------------------
    [Fact]
    public void RemoveDwellings_RenumbersSurvivorsDensely_KeepingTheirIdsAndMessreihen()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 4);
        incident.RecordCoValue(clock, op, buildingId, 0, 3, 120);
        incident.SetDwellingDetails(buildingId, 0, 4, "Schmidt", null);
        var thirdId = Unit(incident, buildingId, 0, 3).Id;
        var fourthId = Unit(incident, buildingId, 0, 4).Id;

        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 2 });

        var floor = incident.Dwellings.Where(d => d.FloorOrdinal == 0).OrderBy(d => d.ApartmentNumber).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, floor.Select(d => d.ApartmentNumber));
        Assert.Equal(3, incident.Buildings[0].ApartmentsFor(0));

        // Whg. 3 became Whg. 2 -- same record, same Messreihe, not a fresh empty unit.
        var movedThird = Unit(incident, buildingId, 0, 2);
        Assert.Equal(thirdId, movedThird.Id);
        Assert.Equal(120, movedThird.CoValue);
        Assert.NotEmpty(movedThird.Readings);

        var movedFourth = Unit(incident, buildingId, 0, 3);
        Assert.Equal(fourthId, movedFourth.Id);
        Assert.Equal("Schmidt", movedFourth.ResidentName);
    }

    [Fact]
    public void RemoveDwellings_LeavesOtherFloorsAlone()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 4);

        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 1, 2 });

        Assert.Equal(2, incident.Dwellings.Count(d => d.FloorOrdinal == 0));
        Assert.Equal(4, incident.Dwellings.Count(d => d.FloorOrdinal == 1));
        Assert.Equal(4, incident.Buildings[0].ApartmentsFor(1));
    }

    [Fact]
    public void RemoveDwellings_CarriesCustomLabelsWithTheirUnit()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 4);
        incident.SetApartmentLabel(buildingId, 0, 1, "Bäckerei");
        incident.SetApartmentLabel(buildingId, 0, 2, "Kiosk");
        incident.SetApartmentLabel(buildingId, 0, 3, "Supermarkt");
        incident.SetApartmentLabel(buildingId, 0, 4, "Kanzlei");

        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 2 });

        var building = incident.Buildings[0];
        Assert.Equal("Bäckerei", CoMeasurementLabels.ApartmentLabel(building, 0, 1));
        Assert.Equal("Supermarkt", CoMeasurementLabels.ApartmentLabel(building, 0, 2));
        Assert.Equal("Kanzlei", CoMeasurementLabels.ApartmentLabel(building, 0, 3));

        // The vacated tail key is gone, so growing the floor again does not resurrect "Kanzlei"
        // on a brand-new empty Wohnung.
        Assert.False(building.HasApartmentLabel(0, 4));
    }

    [Fact]
    public void RemoveDwellings_DoesNotFreezeDefaultLabelsAsOverrides()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 3);

        // A 3-flat floor renders as Links/Mitte/Rechts by default. Removing one must not write
        // those defaults back as if a crew had typed them.
        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 2 });

        Assert.Empty(incident.Buildings[0].ApartmentLabels);
    }

    [Fact]
    public void RemoveDwellings_LogsTheRemovedUnitsByTheLabelTheCrewSaw()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 3);
        incident.RecordCoValue(clock, op, buildingId, 0, 2, 120);

        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 2 });

        var entry = incident.Journal[^1].Text;

        // "Mitte" while the floor still had three units -- not "Whg. 2", which is what reading the
        // label after the count changed would have produced.
        Assert.Contains("Mitte", entry, StringComparison.Ordinal);
        Assert.Contains("120 ppm", entry, StringComparison.Ordinal);
        Assert.Contains("jetzt 2 Wohnungen", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveDwellings_NamesWhatEachRemovedUnitCarried()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 4);
        incident.SetDwellingDetails(buildingId, 0, 4, "Müller", true);

        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 4 });

        var entry = incident.Journal[^1].Text;
        Assert.Contains("Bewohner erfasst", entry, StringComparison.Ordinal);
        Assert.Contains("Schlüssel vorhanden", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveDwellings_RefusesToEmptyAFloor()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 3);

        Assert.Throws<ArgumentException>(() =>
            incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 1, 2, 3 }));
    }

    [Fact]
    public void RemoveDwellings_RefusesAnEmptyOrDuplicateOrUnknownSelection()
    {
        var (incident, clock, op, buildingId) = Scene(apartmentsPerFloor: 3);

        Assert.Throws<ArgumentException>(() =>
            incident.RemoveDwellings(clock, op, buildingId, 0, Array.Empty<int>()));
        Assert.Throws<ArgumentException>(() =>
            incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 2, 2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 9 }));
    }

    [Fact]
    public void RemoveDwellings_RefusesAFloorThatDoesNotExist()
    {
        var (incident, clock, op, buildingId) = Scene();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            incident.RemoveDwellings(clock, op, buildingId, 9, new[] { 1 }));
    }

    // --- #419's sibling: UpdateCoBuildingStructure vs. per-floor overrides (#265) ------------
    [Fact]
    public void AddingAFloor_KeepsAFloorWhoseCountExceedsTheBuildingDefault()
    {
        var (incident, clock, op, buildingId) = Scene(floorCount: 2, apartmentsPerFloor: 3);
        incident.SetApartmentCount(clock, op, buildingId, 0, 14);
        incident.RecordCoValue(clock, op, buildingId, 0, 12, 120);
        incident.SetDwellingDetails(buildingId, 0, 14, "Müller", null);

        // "OG HINZUFÜGEN": grows the building, passing the unchanged default of 3.
        incident.UpdateCoBuildingStructure(clock, op, buildingId, 3, 3, 0);

        Assert.Equal(14, incident.Buildings[0].ApartmentsFor(0));
        Assert.Equal(14, incident.Dwellings.Count(d => d.FloorOrdinal == 0));
        Assert.Equal(120, Unit(incident, buildingId, 0, 12).CoValue);
        Assert.Equal("Müller", Unit(incident, buildingId, 0, 14).ResidentName);
        Assert.Equal(3, incident.Dwellings.Count(d => d.FloorOrdinal == 3));
    }

    [Fact]
    public void AddingAFloor_RestoresWohnungenAnEarlierBuildAlreadyDropped()
    {
        var (incident, clock, op, buildingId) = Scene(floorCount: 1, apartmentsPerFloor: 3);
        incident.SetApartmentCount(clock, op, buildingId, 0, 6);

        // Simulate a file the old bug damaged: the count says 6, only 3 Wohnungen remain.
        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 4, 5, 6 });
        incident.SetApartmentCount(clock, op, buildingId, 0, 6);
        Assert.Equal(6, incident.Dwellings.Count(d => d.FloorOrdinal == 0));
    }

    [Fact]
    public void RemoveDwellings_HealsACountThatOutranTheWohnungen()
    {
        var (incident, clock, op, buildingId) = Scene(floorCount: 1, apartmentsPerFloor: 4);

        incident.RemoveDwellings(clock, op, buildingId, 0, new[] { 4 });

        Assert.Equal(3, incident.Buildings[0].ApartmentsFor(0));
        Assert.Equal(3, incident.Dwellings.Count(d => d.FloorOrdinal == 0));
    }
}
