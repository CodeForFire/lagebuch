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
    public void Incident_UpdateCoBuildingStructure_GrowingFloorCountAddsDwellings()
    {
        // The removal-only pass never created dwellings for a grown structure -- an operator who
        // raised FloorCount via a future structure edit got no new rows to fill in.
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3); // 3 floors (EG..2.OG) * 3 apts = 9

        incident.UpdateCoBuildingStructure(clock, op, incident.Buildings[0].Id, 4, 3);

        Assert.Equal(4, incident.Buildings[0].FloorCount);
        Assert.Equal(15, incident.Dwellings.Count); // 5 floors * 3 apts
        Assert.Contains(incident.Dwellings, d => d.FloorOrdinal == 4);
    }

    // --- Issue #218: Untergeschoss (UG) floors below EG ---------------------------------------
    [Fact]
    public void Building_Create_WithUndergroundFloors_ValidatesRange()
    {
        var building = Building.Create("Haus A", 2, 3, 0, undergroundFloorCount: 2);
        Assert.Equal(2, building.UndergroundFloorCount);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Building.Create("Haus A", 2, 3, 0, undergroundFloorCount: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Building.Create("Haus A", 2, 3, 0, undergroundFloorCount: -1));
    }

    [Fact]
    public void CoMeasurementLabels_FloorLabel_UG()
    {
        Assert.Equal("1. UG", CoMeasurementLabels.FloorLabel(-1));
        Assert.Equal("2. UG", CoMeasurementLabels.FloorLabel(-2));
    }

    [Fact]
    public void Incident_AddCoBuilding_WithUndergroundFloors_CreatesUgDwellings()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);

        incident.AddCoBuilding(clock, op, "Haus A", 2, 3, undergroundFloorCount: 1);

        // EG, 1.OG, 2.OG, 1.UG = 4 floors * 3 apts
        Assert.Equal(12, incident.Dwellings.Count);
        Assert.Contains(incident.Dwellings, d => d.FloorOrdinal == -1);
        Assert.Contains(
            incident.Journal,
            e => e.Text.Contains("1. UG–2. OG", StringComparison.Ordinal));
    }

    [Fact]
    public void Incident_UpdateCoBuildingStructure_AddingUndergroundFloorsAddsDwellings()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3); // no UG yet

        incident.UpdateCoBuildingStructure(clock, op, incident.Buildings[0].Id, 2, 3, undergroundFloorCount: 1);

        Assert.Equal(1, incident.Buildings[0].UndergroundFloorCount);
        Assert.Equal(12, incident.Dwellings.Count); // 4 floors * 3 apts
        Assert.Contains(incident.Dwellings, d => d.FloorOrdinal == -1);
    }

    [Fact]
    public void Incident_UpdateCoBuildingStructure_RemovingUndergroundFloorsRemovesDwellings()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3, undergroundFloorCount: 2);

        incident.UpdateCoBuildingStructure(clock, op, incident.Buildings[0].Id, 2, 3, undergroundFloorCount: 1);

        Assert.Equal(1, incident.Buildings[0].UndergroundFloorCount);
        Assert.DoesNotContain(incident.Dwellings, d => d.FloorOrdinal == -2);
        Assert.Contains(incident.Dwellings, d => d.FloorOrdinal == -1);
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

    // --- Issue #265: per-floor Wohnungen counts and labels (Option B) -------------------------
    [Fact]
    public void Building_ApartmentsFor_UsesDefaultUntilOverridden()
    {
        var building = Building.Create("Haus A", 2, 3, 0);
        Assert.Equal(3, building.ApartmentsFor(0));
        Assert.Equal(3, building.ApartmentsFor(1));

        var updated = building.WithApartmentCount(1, 7);
        Assert.Equal(7, updated.ApartmentsFor(1));
        Assert.Equal(3, updated.ApartmentsFor(0)); // untouched floor keeps the default
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public void Building_WithApartmentCount_InvalidCount_Throws(int count)
    {
        var building = Building.Create("Haus A", 2, 3, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => building.WithApartmentCount(0, count));
    }

    [Fact]
    public void Building_WithApartmentLabel_IsIsolatedPerFloor()
    {
        // Same apartment number (2), two different floors: labeling one must not affect the other
        // — the whole point of #265's per-floor keying.
        var building = Building.Create("Haus A", 2, 3, 0)
            .WithApartmentLabel(0, 2, "Müller")
            .WithApartmentLabel(1, 2, "Schmidt");

        Assert.Equal("Müller", CoMeasurementLabels.ApartmentLabel(building, 0, 2));
        Assert.Equal("Schmidt", CoMeasurementLabels.ApartmentLabel(building, 1, 2));
        Assert.Equal("Mitte", CoMeasurementLabels.ApartmentLabel(building, 2, 2)); // never labeled, defaults for a 3-flat floor
    }

    [Fact]
    public void CoMeasurementLabels_MigrateLegacyApartmentLabels_FansOutAcrossFloors()
    {
        // Pre-#265 saved incidents keyed ApartmentLabels by apartment number alone, meaning
        // "this label applies to every floor's column 2" — loading one must not silently lose it.
        var legacy = new Dictionary<string, string?> { ["2"] = "Müller" };

        var migrated = CoMeasurementLabels.MigrateLegacyApartmentLabels(legacy, floorCount: 1, undergroundFloorCount: 1);

        Assert.Equal("Müller", migrated[CoMeasurementLabels.ApartmentLabelKey(-1, 2)]);
        Assert.Equal("Müller", migrated[CoMeasurementLabels.ApartmentLabelKey(0, 2)]);
        Assert.Equal("Müller", migrated[CoMeasurementLabels.ApartmentLabelKey(1, 2)]);
    }

    [Fact]
    public void CoMeasurementLabels_MigrateLegacyApartmentLabels_LeavesNewFormatKeysAlone()
    {
        var alreadyMigrated = new Dictionary<string, string?> { [CoMeasurementLabels.ApartmentLabelKey(0, 2)] = "Müller" };

        var migrated = CoMeasurementLabels.MigrateLegacyApartmentLabels(alreadyMigrated, floorCount: 2, undergroundFloorCount: 0);

        Assert.Equal("Müller", Assert.Single(migrated).Value);
    }

    [Fact]
    public void Incident_SetApartmentCount_GrowsAndTrimsOnlyThatFloor()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3); // EG,1.OG,2.OG × 3 = 9

        incident.SetApartmentCount(clock, op, incident.Buildings[0].Id, 0, 5);

        Assert.Equal(5, incident.Buildings[0].ApartmentsFor(0));
        Assert.Equal(3, incident.Buildings[0].ApartmentsFor(1)); // other floors untouched
        Assert.Equal(5, incident.Dwellings.Count(d => d.FloorOrdinal == 0));
        Assert.Equal(3, incident.Dwellings.Count(d => d.FloorOrdinal == 1));
        Assert.Contains(incident.Journal, e => e.Text.Contains("jetzt 5 Wohnungen", StringComparison.Ordinal));

        incident.SetApartmentCount(clock, op, incident.Buildings[0].Id, 0, 2);
        Assert.Equal(2, incident.Dwellings.Count(d => d.FloorOrdinal == 0));
    }

    [Fact]
    public void Incident_SetApartmentCount_NonExistentFloor_Throws()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            incident.SetApartmentCount(clock, op, incident.Buildings[0].Id, 5, 4));
    }

    [Fact]
    public void Incident_SetApartmentLabel_DoesNotLogToETB()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", 2, 3);
        var journalCountBefore = incident.Journal.Count;

        incident.SetApartmentLabel(incident.Buildings[0].Id, 0, 1, "Müller");

        Assert.Equal(journalCountBefore, incident.Journal.Count);
        Assert.Equal("Müller", CoMeasurementLabels.ApartmentLabel(incident.Buildings[0], 0, 1));
    }

    [Fact]
    public void CoSeverityClassifier_SeverityOf_Null_IsNormal()
    {
        Assert.Equal(CoSeverity.Normal, CoSeverityClassifier.SeverityOf(null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void CoSeverityClassifier_SeverityOf_BelowElevatedThreshold_IsNormal(int ppm)
    {
        Assert.Equal(CoSeverity.Normal, CoSeverityClassifier.SeverityOf(ppm));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(199)]
    public void CoSeverityClassifier_SeverityOf_InElevatedRange_IsElevated(int ppm)
    {
        Assert.Equal(CoSeverity.Elevated, CoSeverityClassifier.SeverityOf(ppm));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(799)]
    public void CoSeverityClassifier_SeverityOf_InDangerousRange_IsDangerous(int ppm)
    {
        Assert.Equal(CoSeverity.Dangerous, CoSeverityClassifier.SeverityOf(ppm));
    }

    [Theory]
    [InlineData(800)]
    [InlineData(9999)]
    public void CoSeverityClassifier_SeverityOf_AtOrAboveLethalThreshold_IsLethal(int ppm)
    {
        Assert.Equal(CoSeverity.Lethal, CoSeverityClassifier.SeverityOf(ppm));
    }

    [Fact]
    public void CoSeverityClassifier_IsImplausible_Null_IsFalse()
    {
        Assert.False(CoSeverityClassifier.IsImplausible(null));
    }

    [Theory]
    [InlineData(2000)]
    [InlineData(800)]
    public void CoSeverityClassifier_IsImplausible_AtOrBelowThreshold_IsFalse(int ppm)
    {
        Assert.False(CoSeverityClassifier.IsImplausible(ppm));
    }

    [Theory]
    [InlineData(2001)]
    [InlineData(9999)]
    public void CoSeverityClassifier_IsImplausible_AboveThreshold_IsTrue(int ppm)
    {
        Assert.True(CoSeverityClassifier.IsImplausible(ppm));
    }
}
