using LageBuch.Domain;
using LageBuch.Domain.CoMeasurement;
using LageBuch.AppLogic.ViewModels;

namespace LageBuch.AppLogic.Tests;

public class CoMessprotokollViewModelTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));

    private static (LocalIncidentSession session, CoMessprotokollViewModel vm) CreateVm()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(store, Clock, op, Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(), Enumerable.Empty<(string, bool)>());
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

    [Fact]
    public void ViewModel_IsReadOnly_WhenSessionReadOnly()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var path = Path.GetTempFileName();
        LocalIncidentSession.StartNew(store, Clock, op, path,
            Enumerable.Empty<(string, bool)>(), Enumerable.Empty<(string, bool)>());
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

    [Fact]
    public void ViewModel_EmptyState_NoBuildings()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(store, Clock, op, Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(), Enumerable.Empty<(string, bool)>());
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });

        Assert.False(vm.HasBuildings);
        Assert.False(vm.CanModify);
        Assert.Empty(vm.BuildingOptions);
        Assert.Empty(vm.MatrixRows);
    }
}
