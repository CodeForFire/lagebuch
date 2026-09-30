using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Tests;

/// <summary>#443: a Geschoss could be added but never removed. These cover OG/UG ENTFERNEN --
/// where they stop, when they remove silently, and when they stop and ask first.</summary>
public class CoFloorRemovalViewModelTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));

    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm, Guid BuildingId) CreateVm(
        int floorCount = 3, int apartmentsPerFloor = 6, int undergroundFloorCount = 1)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator("Test", null),
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", floorCount, apartmentsPerFloor, undergroundFloorCount);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { })
        {
            IsStructureMode = true,
        };
        return (session, vm, session.Incident.Buildings[0].Id);
    }

    [Fact]
    public void Removing_an_empty_Obergeschoss_happens_without_asking()
    {
        var (session, vm, _) = CreateVm();

        vm.RemoveObergeschossCommand.Execute(null);

        Assert.Null(vm.PendingFloorRemoval);
        Assert.Equal(2, session.Incident.Buildings[0].FloorCount);
        Assert.DoesNotContain(session.Incident.Dwellings, d => d.FloorOrdinal == 3);
        Assert.DoesNotContain(vm.MatrixRows, r => r.Ordinal == 3);
    }

    [Fact]
    public void Removing_an_empty_Untergeschoss_happens_without_asking()
    {
        var (session, vm, _) = CreateVm();

        vm.RemoveUntergeschossCommand.Execute(null);

        Assert.Null(vm.PendingFloorRemoval);
        Assert.Equal(0, session.Incident.Buildings[0].UndergroundFloorCount);
        Assert.DoesNotContain(session.Incident.Dwellings, d => d.FloorOrdinal < 0);
    }

    [Fact]
    public void Removing_a_floor_that_carries_data_asks_and_writes_nothing()
    {
        var (session, vm, buildingId) = CreateVm();
        session.RecordCoValue(buildingId, 3, 2, 120);
        session.SetDwellingDetails(buildingId, 3, 5, "Müller", null);
        var before = session.Incident.Dwellings.Count;

        vm.RemoveObergeschossCommand.Execute(null);

        Assert.True(vm.IsFloorRemovalOpen);
        var pending = vm.PendingFloorRemoval;
        Assert.NotNull(pending);
        Assert.Equal("3. OG ENTFERNEN", pending.Header);
        Assert.Equal("3. OG entfernen? 6 Wohnungen, davon 2 mit erfassten Daten.", pending.Question);
        Assert.Equal(new[] { "Whg. 2 · 120 ppm", "Whg. 5 · Müller" }, pending.Items);
        Assert.Equal(3, session.Incident.Buildings[0].FloorCount);
        Assert.Equal(before, session.Incident.Dwellings.Count);
    }

    [Fact]
    public void A_Wohnung_carrying_only_a_Bezeichnung_makes_the_floor_ask()
    {
        var (session, vm, buildingId) = CreateVm();
        session.SetApartmentLabel(buildingId, -1, 1, "Heizungskeller");

        vm.RemoveUntergeschossCommand.Execute(null);

        Assert.True(vm.IsFloorRemovalOpen);
        Assert.Equal(1, session.Incident.Buildings[0].UndergroundFloorCount);
    }

    [Fact]
    public void Confirming_removes_the_whole_floor()
    {
        var (session, vm, buildingId) = CreateVm();
        session.RecordCoValue(buildingId, 3, 2, 120);

        vm.RemoveObergeschossCommand.Execute(null);
        vm.ConfirmFloorRemovalCommand.Execute(null);

        Assert.False(vm.IsFloorRemovalOpen);
        Assert.Equal(2, session.Incident.Buildings[0].FloorCount);
        Assert.DoesNotContain(session.Incident.Dwellings, d => d.FloorOrdinal == 3);
        Assert.Contains("entfernt: 3. OG (6 Wohnungen: Whg. 2 (120 ppm))", session.Incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    // Opening a tile doesn't make it carry anything, so the silent path must close the sidebar too:
    // FERTIG would otherwise write to a Wohnung that no longer exists.
    [Fact]
    public void Removing_an_empty_floor_closes_an_editor_open_on_it()
    {
        var (_, vm, _) = CreateVm();
        vm.MatrixRows.Single(r => r.Ordinal == 3).Cells[0].OpenEditorCommand.Execute(null);
        Assert.NotNull(vm.Editor);

        vm.RemoveObergeschossCommand.Execute(null);

        Assert.Null(vm.Editor);
        Assert.False(vm.IsEditorOpen);
    }

    [Fact]
    public void Cancelling_leaves_the_structure_unchanged()
    {
        var (session, vm, buildingId) = CreateVm();
        session.RecordCoValue(buildingId, -1, 1, 80);
        var before = session.Incident.Dwellings.Count;

        vm.RemoveUntergeschossCommand.Execute(null);
        vm.CancelFloorRemovalCommand.Execute(null);

        Assert.False(vm.IsFloorRemovalOpen);
        Assert.Equal(1, session.Incident.Buildings[0].UndergroundFloorCount);
        Assert.Equal(before, session.Incident.Dwellings.Count);
    }

    [Fact]
    public void Any_committed_change_drops_the_pending_question()
    {
        var (session, vm, buildingId) = CreateVm();
        session.RecordCoValue(buildingId, 3, 2, 120);
        vm.RemoveObergeschossCommand.Execute(null);
        Assert.NotNull(vm.PendingFloorRemoval);

        session.RecordCoValue(buildingId, 1, 1, 40);

        Assert.Null(vm.PendingFloorRemoval);
    }

    [Fact]
    public void Leaving_structure_mode_drops_the_pending_question()
    {
        var (session, vm, buildingId) = CreateVm();
        session.RecordCoValue(buildingId, 3, 2, 120);
        vm.RemoveObergeschossCommand.Execute(null);

        vm.IsStructureMode = false;

        Assert.Null(vm.PendingFloorRemoval);
        Assert.Equal(3, session.Incident.Buildings[0].FloorCount);
    }

    [Fact]
    public void Switching_Haus_drops_the_pending_question()
    {
        var (session, vm, buildingId) = CreateVm();
        session.RecordCoValue(buildingId, 3, 2, 120);
        session.AddCoBuilding("Haus B", 2, 3);
        vm.RemoveObergeschossCommand.Execute(null);
        Assert.NotNull(vm.PendingFloorRemoval);

        vm.SelectedBuilding = vm.BuildingOptions.Single(b => b.Name == "Haus B");

        Assert.Null(vm.PendingFloorRemoval);
    }

    [Fact]
    public void The_first_Obergeschoss_and_a_missing_Untergeschoss_cannot_be_removed()
    {
        var (_, vm, _) = CreateVm(floorCount: 1, undergroundFloorCount: 0);

        Assert.False(vm.RemoveObergeschossCommand.CanExecute(null));
        Assert.False(vm.RemoveUntergeschossCommand.CanExecute(null));
    }

    [Fact]
    public void The_buttons_follow_the_structure_as_it_shrinks()
    {
        var (_, vm, _) = CreateVm(floorCount: 2, undergroundFloorCount: 1);
        Assert.True(vm.RemoveObergeschossCommand.CanExecute(null));
        Assert.True(vm.RemoveUntergeschossCommand.CanExecute(null));

        vm.RemoveObergeschossCommand.Execute(null);
        vm.RemoveUntergeschossCommand.Execute(null);

        Assert.False(vm.RemoveObergeschossCommand.CanExecute(null));
        Assert.False(vm.RemoveUntergeschossCommand.CanExecute(null));
    }

    [Fact]
    public void A_read_only_session_offers_no_removal()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var path = Path.GetTempFileName();
        var writable = TestSession.StartNew(
            store,
            Clock,
            op,
            path,
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        writable.AddCoBuilding("Haus A", 3, 6, 1);
        var session = LocalIncidentSession.OpenReadOnly(store, Clock, path);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });

        Assert.NotNull(vm.SelectedBuilding);
        Assert.False(vm.RemoveObergeschossCommand.CanExecute(null));
        Assert.False(vm.RemoveUntergeschossCommand.CanExecute(null));
    }
}
