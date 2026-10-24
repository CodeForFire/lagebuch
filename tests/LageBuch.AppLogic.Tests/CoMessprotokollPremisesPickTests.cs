using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// #420: a Haus is picked from the Stammdaten's Objekte instead of typed. The pick fills the
// dialog; what the Lagebuchführer changes there stays changed, and the incident keeps copies.
public class CoMessprotokollPremisesPickTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 10, 24, 10, 0, 0, TimeSpan.Zero));

    private static readonly Premises Lindenallee = AnonymizedExampleData.Premises[0];

    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm) CreateVm(IReadOnlyList<Premises>? premises = null)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator("Test", null),
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        return (session, new CoMessprotokollViewModel(session, Clock, () => { }, premises ?? AnonymizedExampleData.Premises));
    }

    [Fact]
    public void The_dialog_offers_the_objekte_by_name()
    {
        var (_, vm) = CreateVm();

        Assert.Equal(new[] { "Wohnanlage Lindenallee", AnonymizedExampleData.BuildingName }, vm.PremisesOptions);
    }

    [Fact]
    public void Picking_an_objekt_fills_its_structure_and_says_where_it_is()
    {
        var (_, vm) = CreateVm();

        vm.NewBuildingName = "wohnanlage lindenallee ";

        Assert.Equal((4, 3, 1), (vm.NewBuildingFloors, vm.NewBuildingApartments, vm.NewBuildingUndergroundFloors));
        Assert.Equal("Aus den Stammdaten · Lindenallee 12, 12345 Musterstadt · 4 Wohnungsbezeichnungen", vm.NewBuildingPremisesHint);
    }

    [Fact]
    public void The_hint_leaves_out_an_address_and_labels_the_objekt_does_not_have()
    {
        var (_, vm) = CreateVm(new[] { new Premises("Haus B") });

        vm.NewBuildingName = "Haus B";

        Assert.Equal("Aus den Stammdaten", vm.NewBuildingPremisesHint);
    }

    [Fact]
    public void The_hint_counts_a_single_label_in_the_singular()
    {
        var (_, vm) = CreateVm(new[] { new Premises("Haus B") { ApartmentLabels = new Dictionary<string, string?> { ["0:1"] = "Kiosk" } } });

        vm.NewBuildingName = "Haus B";

        Assert.Equal("Aus den Stammdaten · 1 Wohnungsbezeichnung", vm.NewBuildingPremisesHint);
    }

    [Fact]
    public void Adding_a_picked_objekt_creates_the_haus_with_its_labels()
    {
        var (session, vm) = CreateVm();
        vm.NewBuildingName = Lindenallee.Name;

        vm.ConfirmAddBuildingCommand.Execute(null);

        var building = Assert.Single(session.Incident.Buildings);
        Assert.Equal("Wohnanlage Lindenallee", building.Name);
        Assert.Equal((4, 3, 1), (building.FloorCount, building.ApartmentsPerFloor, building.UndergroundFloorCount));
        Assert.Equal("Hausmeister", CoMeasurementLabels.ApartmentLabel(building, 0, 1));
        Assert.Equal("Waschküche", CoMeasurementLabels.ApartmentLabel(building, -1, 2));
    }

    [Fact]
    public void A_count_changed_by_hand_survives_picking_an_objekt()
    {
        var (_, vm) = CreateVm();
        vm.NewBuildingFloors = 12;

        vm.NewBuildingName = Lindenallee.Name;

        Assert.Equal(12, vm.NewBuildingFloors);
        Assert.Equal(3, vm.NewBuildingApartments);
    }

    [Fact]
    public void Picking_a_second_objekt_replaces_what_the_first_one_filled()
    {
        var (_, vm) = CreateVm();
        vm.NewBuildingName = Lindenallee.Name;

        vm.NewBuildingName = AnonymizedExampleData.BuildingName;

        Assert.Equal((3, 2, 0), (vm.NewBuildingFloors, vm.NewBuildingApartments, vm.NewBuildingUndergroundFloors));
    }

    [Fact]
    public void A_label_outside_a_structure_shrunk_by_hand_is_not_created()
    {
        var (session, vm) = CreateVm();
        vm.NewBuildingName = Lindenallee.Name;
        vm.NewBuildingUndergroundFloors = 0;

        Assert.Equal("Aus den Stammdaten · Lindenallee 12, 12345 Musterstadt · 2 Wohnungsbezeichnungen", vm.NewBuildingPremisesHint);
        vm.ConfirmAddBuildingCommand.Execute(null);

        Assert.Equal(new[] { "0:1", "0:3" }, session.Incident.Buildings[0].ApartmentLabels.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_that_is_no_objekt_adds_a_plain_haus_as_before()
    {
        var (session, vm) = CreateVm();
        vm.NewBuildingName = Lindenallee.Name;

        vm.NewBuildingName = "Wohnanlage Lindenallee Rückgebäude";
        vm.ConfirmAddBuildingCommand.Execute(null);

        Assert.Null(vm.NewBuildingPremisesHint);
        var building = Assert.Single(session.Incident.Buildings);
        Assert.Equal("Wohnanlage Lindenallee Rückgebäude", building.Name);
        Assert.Empty(building.ApartmentLabels);
    }

    [Fact]
    public void The_dialog_resets_to_the_defaults_after_adding_a_picked_objekt()
    {
        var (_, vm) = CreateVm();
        vm.NewBuildingName = Lindenallee.Name;

        vm.ConfirmAddBuildingCommand.Execute(null);

        Assert.Equal(string.Empty, vm.NewBuildingName);
        Assert.Equal((8, 10, 1), (vm.NewBuildingFloors, vm.NewBuildingApartments, vm.NewBuildingUndergroundFloors));
        Assert.Null(vm.NewBuildingPremisesHint);
    }

    // #337: the incident holds copies. Whatever later happens to the Objekt in the Stammdaten, the
    // Haus reads as it was created.
    [Fact]
    public void The_haus_keeps_its_labels_when_the_objekt_is_later_changed()
    {
        var premises = new List<Premises> { Lindenallee };
        var (session, vm) = CreateVm(premises);
        vm.NewBuildingName = Lindenallee.Name;
        vm.ConfirmAddBuildingCommand.Execute(null);

        premises.Clear();
        premises.Add(Lindenallee with { FloorCount = 1, ApartmentLabels = new Dictionary<string, string?>() });

        var building = Assert.Single(session.Incident.Buildings);
        Assert.Equal(4, building.FloorCount);
        Assert.Equal("Hausmeister", CoMeasurementLabels.ApartmentLabel(building, 0, 1));
    }

    [Fact]
    public void Without_objekte_the_dialog_offers_nothing_and_adds_as_before()
    {
        var (session, vm) = CreateVm(Array.Empty<Premises>());
        vm.NewBuildingName = "Haus B";

        vm.ConfirmAddBuildingCommand.Execute(null);

        Assert.Empty(vm.PremisesOptions);
        Assert.Equal((8, 10, 1), (session.Incident.Buildings[0].FloorCount, session.Incident.Buildings[0].ApartmentsPerFloor, session.Incident.Buildings[0].UndergroundFloorCount));
    }
}
