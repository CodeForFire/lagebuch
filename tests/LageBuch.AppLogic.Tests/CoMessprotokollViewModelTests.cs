using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;

namespace LageBuch.AppLogic.Tests;

public class CoMessprotokollViewModelTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));

    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm) CreateVm()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            Clock,
            op,
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", 2, 3);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });
        return (session, vm);
    }

    [Fact]
    public void ViewModel_BuildsMatrix_FromIncident()
    {
        var (session, vm) = CreateVm();

        Assert.Single(vm.BuildingOptions);
        Assert.Equal("Haus A", vm.BuildingOptions[0].Name);
        Assert.Equal(3, vm.MatrixRows.Count); // 2 OG + EG
        Assert.Equal(3, vm.ApartmentColumns.Count);
    }

    // --- Issue #218: Untergeschoss (UG) floors below EG --------------------------------------
    [Fact]
    public void AddUntergeschoss_AddsOneFloorBelowEgAndItsDwellings()
    {
        var (session, vm) = CreateVm();

        vm.AddUntergeschossCommand.Execute(null);

        var building = session.Incident.Buildings[0];
        Assert.Equal(1, building.UndergroundFloorCount);
        Assert.Equal(4, vm.MatrixRows.Count); // 2 OG + EG + 1 UG
        Assert.Equal(-1, vm.MatrixRows[^1].Ordinal); // UG sorts below EG
        Assert.Contains(session.Incident.Dwellings, d => d.FloorOrdinal == -1);
    }

    [Fact]
    public void AddUntergeschoss_IsDisabled_AtTheThreeFloorCap()
    {
        var (_, vm) = CreateVm();

        vm.AddUntergeschossCommand.Execute(null);
        vm.AddUntergeschossCommand.Execute(null);
        vm.AddUntergeschossCommand.Execute(null);

        Assert.False(vm.AddUntergeschossCommand.CanExecute(null));
    }

    // Untergeschosse don't show in the matrix until scrolled to, unlike ground/upper floors, so
    // ConfirmAddBuildingCommand defaults NewBuildingUndergroundFloors to 1 rather than 0 -- a
    // building created via the dialog already has a basement to record without a separate
    // UG HINZUFÜGEN click.
    [Fact]
    public void ConfirmAddBuildingCommand_DefaultsToOneUndergroundFloor()
    {
        var (session, vm) = CreateVm();
        vm.NewBuildingName = "Haus B";

        vm.ConfirmAddBuildingCommand.Execute(null);

        var building = session.Incident.Buildings.Single(b => b.Name == "Haus B");
        Assert.Equal(1, building.UndergroundFloorCount);
        Assert.Equal(1, vm.NewBuildingUndergroundFloors); // resets to the default, not 0
    }

    [Fact]
    public void ConfirmAddBuildingCommand_UsesTheChosenUndergroundFloorCount()
    {
        var (session, vm) = CreateVm();
        vm.NewBuildingName = "Haus B";
        vm.NewBuildingUndergroundFloors = 0;

        vm.ConfirmAddBuildingCommand.Execute(null);

        var building = session.Incident.Buildings.Single(b => b.Name == "Haus B");
        Assert.Equal(0, building.UndergroundFloorCount);
    }

    [Fact]
    public void ViewModel_IsReadOnly_WhenSessionReadOnly()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var path = Path.GetTempFileName();
        LocalIncidentSession.StartNew(
            store,
            Clock,
            op,
            path,
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        var session = LocalIncidentSession.OpenReadOnly(store, Clock, path);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });

        Assert.True(vm.IsReadOnly);
    }

    [Fact]
    public void DwellingCellVM_StatusBrush_MatchesStatus()
    {
        var building = Building.Create("Haus A", 2, 3, 0);
        var dwelling = Dwelling.Create(building.Id, 0, 1);

        var cell = new DwellingCellViewModel(dwelling, building, false, (_, _, _, _) => { }, (_, _, _, _) => { }, (_, _, _) => { });

        Assert.Equal("#FFC000", cell.StatusBrush); // NotSearched = Gelb

        cell.Status = DwellingStatus.Searched;
        Assert.Equal("#92D050", cell.StatusBrush); // Searched = Grün

        cell.Status = DwellingStatus.Affected;
        Assert.Equal("#FF0000", cell.StatusBrush); // Affected = Rot
    }

    [Fact]
    public void DwellingCellVM_CoDisplay_ShowsPlaceholderWhenNull()
    {
        var building = Building.Create("Haus A", 2, 3, 0);
        var dwelling = Dwelling.Create(building.Id, 0, 1);

        var cell = new DwellingCellViewModel(dwelling, building, false, (_, _, _, _) => { }, (_, _, _, _) => { }, (_, _, _) => { });

        Assert.Equal("Kein Messwert", cell.CoDisplay);

        cell.CoValue = 45;
        Assert.Equal("45 ppm", cell.CoDisplay);
    }

    // Every other Add/Confirm command in the app (AddForce, AddRole, ConfirmTransfer, AddTrupp,
    // AddTask, AddEntry, ConfirmIncidentNumber...) gates on its required text field being
    // non-empty. ConfirmAddBuildingCommand had no such gate: clicking HINZUFÜGEN with an empty
    // Hausname called straight through to Building.Create, which throws ArgumentException and
    // takes the whole desktop app down (unhandled on the UI thread).
    [Fact]
    public void ConfirmAddBuildingCommand_IsDisabled_WhenNameIsEmpty()
    {
        var (_, vm) = CreateVm();
        vm.NewBuildingName = string.Empty;

        Assert.False(vm.ConfirmAddBuildingCommand.CanExecute(null));

        vm.NewBuildingName = "Haus B";
        Assert.True(vm.ConfirmAddBuildingCommand.CanExecute(null));

        vm.NewBuildingName = "   ";
        Assert.False(vm.ConfirmAddBuildingCommand.CanExecute(null));
    }

    [Fact]
    public void ViewModel_EmptyState_NoBuildings()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            Clock,
            op,
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });

        Assert.False(vm.HasBuildings);
        Assert.False(vm.CanModify);
        Assert.Empty(vm.BuildingOptions);
        Assert.Empty(vm.MatrixRows);
    }
}
