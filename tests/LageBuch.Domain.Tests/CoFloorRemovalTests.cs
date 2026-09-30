namespace LageBuch.Domain.Tests;

/// <summary>#443: OG/UG ENTFERNEN shrink a building by one floor through
/// <see cref="Incident.UpdateCoBuildingStructure"/>. These cover what goes, what stays, and that
/// the ETB names the floor and what was on it rather than just the new floor count.</summary>
public class CoFloorRemovalTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static (Incident Incident, FixedClock Clock, SessionOperator Op, Guid BuildingId) Scene(
        int floorCount = 3, int apartmentsPerFloor = 3, int undergroundFloorCount = 1)
    {
        var clock = new FixedClock(At);
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Haus A", floorCount, apartmentsPerFloor, undergroundFloorCount);
        return (incident, clock, op, incident.Buildings[0].Id);
    }

    [Fact]
    public void Removing_the_top_Obergeschoss_drops_only_its_Wohnungen()
    {
        var (incident, clock, op, buildingId) = Scene();
        incident.RecordCoValue(clock, op, buildingId, 2, 1, 40);

        incident.UpdateCoBuildingStructure(clock, op, buildingId, 2, 3, 1);

        Assert.Equal(2, incident.Buildings[0].FloorCount);
        Assert.DoesNotContain(incident.Dwellings, d => d.FloorOrdinal == 3);
        Assert.Equal(12, incident.Dwellings.Count);
        Assert.Equal(40, incident.Dwellings.Single(d => d.FloorOrdinal == 2 && d.ApartmentNumber == 1).CoValue);
    }

    [Fact]
    public void Removing_the_last_Untergeschoss_leaves_none()
    {
        var (incident, clock, op, buildingId) = Scene(undergroundFloorCount: 1);

        incident.UpdateCoBuildingStructure(clock, op, buildingId, 3, 3, 0);

        Assert.Equal(0, incident.Buildings[0].UndergroundFloorCount);
        Assert.DoesNotContain(incident.Dwellings, d => d.FloorOrdinal < 0);
    }

    [Fact]
    public void The_ETB_names_the_removed_floor_and_each_Wohnung_that_carried_something()
    {
        var (incident, clock, op, buildingId) = Scene();
        incident.RecordCoValue(clock, op, buildingId, 3, 2, 120);
        incident.SetDwellingDetails(buildingId, 3, 3, "Müller", null);

        incident.UpdateCoBuildingStructure(clock, op, buildingId, 2, 3, 1);

        Assert.Equal(
            "CO-Struktur geändert: Haus A jetzt 1. UG–2. OG, 3 Wohnungen je Geschoss, entfernt: 3. OG (3 Wohnungen: Mitte (120 ppm), Rechts (Bewohner erfasst))",
            incident.Journal[^1].Text);
    }

    [Fact]
    public void The_ETB_calls_an_empty_removed_floor_leer_and_does_not_count_its_Wohnungen_twice()
    {
        var (incident, clock, op, buildingId) = Scene();

        incident.UpdateCoBuildingStructure(clock, op, buildingId, 3, 3, 0);

        var entry = incident.Journal[^1].Text;
        Assert.EndsWith(", entfernt: 1. UG (3 Wohnungen, leer)", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("Wohnungen entfernt", entry, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Wohnung_carrying_only_a_Bezeichnung_is_named_in_the_ETB()
    {
        var (incident, clock, op, buildingId) = Scene();
        incident.SetApartmentLabel(buildingId, 3, 1, "Kiosk");

        incident.UpdateCoBuildingStructure(clock, op, buildingId, 2, 3, 1);

        Assert.EndsWith(", entfernt: 3. OG (3 Wohnungen: Kiosk)", incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    // The side effect #442 promised: WithStructure leaves the per-floor count in place, so an
    // accidentally removed 3. OG with its own count comes back in the right shape -- empty.
    [Fact]
    public void Adding_a_removed_floor_back_restores_its_own_Wohnungen_count()
    {
        var (incident, clock, op, buildingId) = Scene();
        incident.SetApartmentCount(clock, op, buildingId, 3, 14);
        incident.RecordCoValue(clock, op, buildingId, 3, 14, 80);

        incident.UpdateCoBuildingStructure(clock, op, buildingId, 2, 3, 1);
        incident.UpdateCoBuildingStructure(clock, op, buildingId, 3, 3, 1);

        var restored = incident.Dwellings.Where(d => d.FloorOrdinal == 3).ToList();
        Assert.Equal(14, restored.Count);
        Assert.All(restored, d => Assert.False(d.HasData));
    }

    [Fact]
    public void The_Erdgeschoss_and_first_Obergeschoss_cannot_be_removed()
    {
        var (incident, clock, op, buildingId) = Scene(floorCount: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            incident.UpdateCoBuildingStructure(clock, op, buildingId, 0, 3, 1));
        Assert.Equal(1, incident.Buildings[0].FloorCount);
    }
}
