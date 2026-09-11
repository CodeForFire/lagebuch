using System.Collections.Specialized;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.ValueObjects;
using LageBuch.Sync;

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
        Assert.All(vm.MatrixRows, r => Assert.Equal(3, r.Cells.Count));
    }

    // --- Issue #265: per-floor Wohnungen counts and labels (Option B) ------------------------
    [Fact]
    public void ChangingApartmentCount_OnOneFloor_RebuildsOnlyThatRow()
    {
        var (session, vm) = CreateVm();
        var egRow = vm.MatrixRows.Single(r => r.Ordinal == 0);

        egRow.ApartmentCount = 5;

        var updatedEgRow = vm.MatrixRows.Single(r => r.Ordinal == 0);
        Assert.Equal(5, updatedEgRow.Cells.Count);
        var ogRow = vm.MatrixRows.Single(r => r.Ordinal == 1);
        Assert.Equal(3, ogRow.Cells.Count); // untouched floor
        Assert.Equal(5, session.Incident.Buildings[0].ApartmentsFor(0));
    }

    // #242: this used to assert the label persisted on CloseEditor (ABBRECHEN) -- back when cancel
    // and confirm ran the same persist. FERTIG is the commit point now.
    [Fact]
    public void EditingLabel_InTheEditor_PersistsOnConfirm()
    {
        var (session, vm) = CreateVm();
        var cell = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[1];

        cell.OpenEditorCommand.Execute(null);
        vm.Editor!.Label = "Müller";
        vm.ConfirmEditorCommand.Execute(null);

        var building = session.Incident.Buildings[0];
        Assert.Equal("Müller", CoMeasurementLabels.ApartmentLabel(building, 0, cell.ApartmentNumber));

        // A same-numbered unit on a different floor is untouched (#265's whole point).
        Assert.NotEqual("Müller", CoMeasurementLabels.ApartmentLabel(building, 1, cell.ApartmentNumber));
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

    // --- Issue #258: Obergeschoss (OG) floors above the current top floor -------------------
    [Fact]
    public void AddObergeschoss_AddsOneFloorAboveTopAndItsDwellings()
    {
        var (session, vm) = CreateVm();

        vm.AddObergeschossCommand.Execute(null);

        var building = session.Incident.Buildings[0];
        Assert.Equal(3, building.FloorCount);
        Assert.Equal(4, vm.MatrixRows.Count); // 3 OG + EG
        Assert.Equal(3, vm.MatrixRows[0].Ordinal); // new OG sorts above the previous top floor
        Assert.Contains(session.Incident.Dwellings, d => d.FloorOrdinal == 3);
    }

    [Fact]
    public void AddObergeschoss_IsDisabled_AtTheFiftyFloorCap()
    {
        var (_, vm) = CreateVm();
        vm.NewBuildingName = "Haus B";
        vm.NewBuildingFloors = 50;
        vm.ConfirmAddBuildingCommand.Execute(null);
        vm.SelectedBuilding = vm.BuildingOptions.Single(b => b.Name == "Haus B");

        Assert.False(vm.AddObergeschossCommand.CanExecute(null));
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

        var cell = new DwellingCellViewModel(dwelling, building, false, (_, _, _) => { });

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

        var cell = new DwellingCellViewModel(dwelling, building, false, (_, _, _) => { });

        Assert.Equal("Kein Messwert", cell.CoDisplay);

        cell.CoValue = 45;
        Assert.Equal("45 ppm", cell.CoDisplay);
    }

    [Fact]
    public void DwellingCellVM_CoSeverityFlags_MatchThresholds()
    {
        var building = Building.Create("Haus A", 2, 3, 0);
        var dwelling = Dwelling.Create(building.Id, 0, 1);
        var cell = new DwellingCellViewModel(dwelling, building, false, (_, _, _) => { });

        AssertNoSeverityFlag(cell);

        cell.CoValue = 30;
        Assert.True(cell.IsCoElevated);
        Assert.False(cell.IsCoDangerous);
        Assert.False(cell.IsCoLethal);

        cell.CoValue = 200;
        Assert.False(cell.IsCoElevated);
        Assert.True(cell.IsCoDangerous);
        Assert.False(cell.IsCoLethal);

        cell.CoValue = 800;
        Assert.False(cell.IsCoElevated);
        Assert.False(cell.IsCoDangerous);
        Assert.True(cell.IsCoLethal);

        static void AssertNoSeverityFlag(DwellingCellViewModel cell)
        {
            Assert.False(cell.IsCoElevated);
            Assert.False(cell.IsCoDangerous);
            Assert.False(cell.IsCoLethal);
        }
    }

    [Fact]
    public void DwellingEditorVM_IsCoImplausible_FlagsOnlyValuesAboveThreshold()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);

        editor.CoValue = 2000;
        Assert.False(editor.IsCoImplausible);

        editor.CoValue = 2001;
        Assert.True(editor.IsCoImplausible);
    }

    [Fact]
    public void DwellingEditorVM_IsCoLethal_ReflectsLiveEdit()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);

        editor.CoValue = 799;
        Assert.False(editor.IsCoLethal);

        editor.CoValue = 800;
        Assert.True(editor.IsCoLethal);
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

    // Reproduces issue: entering a ppm value on a joined/synced device switched the selected Haus
    // to a different one. RemoteIncidentSession replaces its whole Incident/Building object graph
    // via SnapshotMapper on every broadcast, including the echo of the client's own edit; the fresh
    // Building instances carry brand-new FloorDescriptions/ApartmentLabels dictionaries every time,
    // and Building (a record) falls back to reference equality for those, so the previously
    // selected Building never compares equal to its freshly rehydrated counterpart. This test
    // stands in for RemoteIncidentSession without the network stack, by round-tripping the Incident
    // through SnapshotMapper on every change exactly like the real session does.
    [Fact]
    public void SelectedBuilding_StaysSelected_AfterEnteringPpm_WithMultipleBuildings()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var local = LocalIncidentSession.StartNew(
            store,
            Clock,
            op,
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        local.AddCoBuilding("Haus A", 2, 3);
        local.AddCoBuilding("Haus B", 2, 3);

        var session = new SnapshotRoundTrippingSession(local);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });

        var houseB = vm.BuildingOptions.Single(b => b.Name == "Haus B");
        vm.SelectedBuilding = houseB;

        // #242: a ppm value now reaches the session only via the sidebar's FERTIG.
        var cell = vm.MatrixRows.SelectMany(r => r.Cells).First(c => c.BuildingId == houseB.Id);
        cell.OpenEditorCommand.Execute(null);
        vm.Editor!.CoValue = 45;
        vm.ConfirmEditorCommand.Execute(null); // triggers a RecordCoValue round trip

        Assert.Equal("Haus B", vm.SelectedBuilding?.Name);
    }

    // Reproduces the actual live bug (confirmed by driving the real desktop app): the "HAUS"
    // ComboBox is two-way bound to SelectedBuilding, with BuildingOptions as its ItemsSource
    // (SelectedItem="{Binding SelectedBuilding}"). Clearing an ObservableCollection fires a Reset
    // notification, and Avalonia's Selector reacts to that by synchronously nulling its own
    // SelectedItem — which pushes back through the two-way binding and sets SelectedBuilding to
    // null. A plain unit test with no live control attached to BuildingOptions can't see this at
    // all (nothing reacts to the Reset), so this test attaches a minimal stand-in that reproduces
    // exactly that one piece of real Selector behavior.
    [Fact]
    public void SelectedBuilding_StaysSelected_WhenBuildingOptionsResetsLikeARealBoundComboBox()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var local = LocalIncidentSession.StartNew(
            store,
            Clock,
            op,
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        local.AddCoBuilding("Haus A", 2, 3);
        local.AddCoBuilding("Haus B", 2, 3);

        var vm = new CoMessprotokollViewModel(local, Clock, () => { });

        var houseB = vm.BuildingOptions.Single(b => b.Name == "Haus B");
        vm.SelectedBuilding = houseB;

        vm.BuildingOptions.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                vm.SelectedBuilding = null;
            }
        };

        var cell = vm.MatrixRows.SelectMany(r => r.Cells).First(c => c.BuildingId == houseB.Id);
        cell.OpenEditorCommand.Execute(null);
        vm.Editor!.CoValue = 45;
        vm.ConfirmEditorCommand.Execute(null);

        Assert.Equal("Haus B", vm.SelectedBuilding?.Name);
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

    // --- Issue #242: ABBRECHEN must discard, FERTIG must commit ------------------------------
    // Before this, ABBRECHEN and FERTIG ran the same PersistSelectedCellDetails(), and Status and
    // CO-Wert were written straight through on every click/keystroke -- so there was no way to
    // discard an edit, and every intermediate ppm value was already in the Einsatztagebuch.
    private static DwellingCellViewModel Cell(CoMessprotokollViewModel vm, int floor, int apartment) =>
        vm.MatrixRows.SelectMany(r => r.Cells)
            .First(c => c.FloorOrdinal == floor && c.ApartmentNumber == apartment);

    private static DwellingEditorViewModel OpenEditor(CoMessprotokollViewModel vm, int floor, int apartment)
    {
        Cell(vm, floor, apartment).OpenEditorCommand.Execute(null);
        return vm.Editor!;
    }

    private static Dwelling DwellingOf(LocalIncidentSession session, Guid buildingId, int floor, int apartment) =>
        session.Incident.Dwellings.First(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == floor && d.ApartmentNumber == apartment);

    [Fact]
    public void CancelEditor_DiscardsEveryField()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        var editor = OpenEditor(vm, 0, 1);

        vm.SetEditorStatusAffectedCommand.Execute(null);
        editor.CoValue = 120;
        editor.ResidentName = "Musterfrau";
        editor.KeyAvailable = true;
        editor.Label = "Hinterhaus";

        vm.CloseEditorCommand.Execute(null);

        var dwelling = DwellingOf(session, buildingId, 0, 1);
        Assert.Equal(DwellingStatus.NotSearched, dwelling.Status);
        Assert.Null(dwelling.CoValue);
        Assert.Null(dwelling.ResidentName);
        Assert.Null(dwelling.KeyAvailable);
        Assert.Equal("Links", CoMeasurementLabels.ApartmentLabel(session.Incident.Buildings[0], 0, 1));
        Assert.False(vm.IsEditorOpen);
        Assert.Null(vm.Editor);
    }

    [Fact]
    public void CancelEditor_WritesNoJournalEntry()
    {
        var (session, vm) = CreateVm();
        var before = session.Incident.Journal.Count;
        var editor = OpenEditor(vm, 0, 1);

        vm.SetEditorStatusAffectedCommand.Execute(null);
        editor.CoValue = 120;
        vm.CloseEditorCommand.Execute(null);

        Assert.Equal(before, session.Incident.Journal.Count);
    }

    [Fact]
    public void ConfirmEditor_CommitsEveryField()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        var editor = OpenEditor(vm, 0, 1);

        vm.SetEditorStatusAffectedCommand.Execute(null);
        editor.CoValue = 120;
        editor.ResidentName = "Musterfrau";
        editor.KeyAvailable = true;
        editor.Label = "Hinterhaus";

        vm.ConfirmEditorCommand.Execute(null);

        var dwelling = DwellingOf(session, buildingId, 0, 1);
        Assert.Equal(DwellingStatus.Affected, dwelling.Status);
        Assert.Equal(120, dwelling.CoValue);
        Assert.Equal("Musterfrau", dwelling.ResidentName);
        Assert.True(dwelling.KeyAvailable);
        Assert.Equal("Hinterhaus", CoMeasurementLabels.ApartmentLabel(session.Incident.Buildings[0], 0, 1));
        Assert.False(vm.IsEditorOpen);
        Assert.Null(vm.Editor);
    }

    // The reason the buffer exists: the journal records the reading the crew settled on, once,
    // not every number they passed through on the way there.
    [Fact]
    public void ConfirmEditor_LogsOneJournalEntryPerChangedField()
    {
        var (session, vm) = CreateVm();
        var before = session.Incident.Journal.Count;
        var editor = OpenEditor(vm, 0, 1);

        vm.SetEditorStatusSearchedCommand.Execute(null);
        editor.CoValue = 8;
        editor.CoValue = 80; // typo, corrected before FERTIG
        editor.CoValue = 45;
        editor.ResidentName = "Musterfrau"; // details are silent -- no journal entry

        vm.ConfirmEditorCommand.Execute(null);

        var added = session.Incident.Journal.Skip(before).Select(e => e.Text).ToList();
        Assert.Equal(2, added.Count);
        Assert.Contains(added, t => t.StartsWith("Whg.-Status", StringComparison.Ordinal));
        Assert.Contains(added, t => t.EndsWith("45 ppm", StringComparison.Ordinal));
        Assert.DoesNotContain(added, t => t.Contains("8 ppm", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfirmEditor_WithNothingChanged_WritesNothing()
    {
        var (session, vm) = CreateVm();
        OpenEditor(vm, 0, 1);
        var before = session.Incident.Journal.Count;
        var changes = 0;
        session.Changed += () => changes++;

        vm.ConfirmEditorCommand.Execute(null);

        Assert.Equal(before, session.Incident.Journal.Count);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void OpenEditor_SeedsTheBufferFromTheDwelling()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.SetDwellingStatus(buildingId, 1, 2, DwellingStatus.Affected);
        session.RecordCoValue(buildingId, 1, 2, 300);

        var editor = OpenEditor(vm, 1, 2);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal(DwellingStatus.Affected, editor.Status);
        Assert.Equal(300, editor.CoValue);
        Assert.Equal("Mitte", editor.Label); // 3 units per floor => Links/Mitte/Rechts
    }

    // The sidebar buffers, but the crew still needs to see the door mark change as they work.
    [Fact]
    public void PendingEdit_PreviewsOnTheTile_AndRevertsOnCancel()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);

        vm.SetEditorStatusAffectedCommand.Execute(null);
        editor.CoValue = 120;

        var pending = Cell(vm, 0, 1);
        Assert.Equal(DwellingStatus.Affected, pending.Status);
        Assert.Equal("#FF0000", pending.StatusBrush);
        Assert.Equal("120 ppm", pending.CoDisplay);

        vm.CloseEditorCommand.Execute(null);

        var reverted = Cell(vm, 0, 1);
        Assert.Equal(DwellingStatus.NotSearched, reverted.Status);
        Assert.Equal("Kein Messwert", reverted.CoDisplay);
    }

    // BuildMatrix throws away and recreates every tile VM on each session change, so an unrelated
    // (or, on a joined device, remote) edit landing mid-edit would otherwise wipe the preview.
    [Fact]
    public void PendingEdit_SurvivesAnUnrelatedSessionChange()
    {
        var (session, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);
        vm.SetEditorStatusAffectedCommand.Execute(null);

        session.AddCoBuilding("Haus B", 1, 2);

        Assert.True(vm.IsEditorOpen);
        Assert.Equal(DwellingStatus.Affected, editor.Status);
        Assert.Equal(DwellingStatus.Affected, Cell(vm, 0, 1).Status);
    }

    [Fact]
    public void SwitchingBuilding_DiscardsThePendingEdit()
    {
        var (session, vm) = CreateVm();
        var hausA = session.Incident.Buildings[0].Id;
        session.AddCoBuilding("Haus B", 1, 2);
        OpenEditor(vm, 0, 1);
        vm.SetEditorStatusAffectedCommand.Execute(null);

        vm.SelectedBuilding = vm.BuildingOptions.Single(b => b.Name == "Haus B");

        Assert.False(vm.IsEditorOpen);
        Assert.Null(vm.Editor);
        Assert.Equal(DwellingStatus.NotSearched, DwellingOf(session, hausA, 0, 1).Status);
    }

    // A closed incident's session throws on any mutation, so FERTIG must not reach it at all --
    // the guard used to live on the tile VM's write-through, which is gone.
    [Fact]
    public void ConfirmEditor_OnReadOnlySession_WritesNothingAndDoesNotThrow()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var path = Path.GetTempFileName();
        var writable = LocalIncidentSession.StartNew(
            store,
            Clock,
            op,
            path,
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        writable.AddCoBuilding("Haus A", 2, 3);
        var session = LocalIncidentSession.OpenReadOnly(store, Clock, path);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { });
        var buildingId = session.Incident.Buildings[0].Id;

        var editor = OpenEditor(vm, 0, 1);
        editor.CoValue = 45;
        editor.Status = DwellingStatus.Affected;
        vm.ConfirmEditorCommand.Execute(null);

        var dwelling = DwellingOf(session, buildingId, 0, 1);
        Assert.Null(dwelling.CoValue);
        Assert.Equal(DwellingStatus.NotSearched, dwelling.Status);
    }
}

/// <summary>
/// Stands in for <see cref="RemoteIncidentSession"/> in a unit test, without the network stack:
/// wraps a <see cref="LocalIncidentSession"/> for the actual mutation logic, but — like the real
/// remote session receiving a host broadcast — replaces its own cached <see cref="Incident"/> with
/// a full <see cref="SnapshotMapper"/> round trip on every change, before raising <see cref="Changed"/>.
/// </summary>
internal sealed class SnapshotRoundTrippingSession : IIncidentSession
{
    private readonly LocalIncidentSession _inner;

    public SnapshotRoundTrippingSession(LocalIncidentSession inner)
    {
        _inner = inner;
        Incident = Roundtrip(_inner.Incident);
        _inner.Changed += () =>
        {
            Incident = Roundtrip(_inner.Incident);
            Changed?.Invoke();
        };
    }

    private static Incident Roundtrip(Incident incident) =>
        SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(SyncJson.Serialize(SnapshotMapper.ToSnapshot(incident))));

    public Incident Incident { get; private set; }

    public SessionOperator? Operator => _inner.Operator;

    public bool IsReadOnly => _inner.IsReadOnly;

    public bool IsRemote => true;

    public event Action? Changed;

    public void AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null) =>
        _inner.AddJournalEntry(direction, text, from, to);

    public void EditJournalEntry(Guid entryId, string text) => _inner.EditJournalEntry(entryId, text);

    public void ToggleChecklistItem(Guid itemId) => _inner.ToggleChecklistItem(itemId);

    public void AssignRole(
        string role,
        string personName,
        string? callSign = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? section = null,
        string? phone = null) =>
        _inner.AssignRole(role, personName, callSign, from, to, section, phone);

    public void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null) =>
        _inner.TransferRole(assignmentId, newPersonName, newCallSign, newPhone);

    public void EditRolePhone(Guid assignmentId, string? phone) => _inner.EditRolePhone(assignmentId, phone);

    public void AddForceUnit(
        string brigade,
        int personnelCount,
        string? callSign = null,
        string? status = null,
        string? notes = null,
        int scbaCount = 0,
        int officerCount = 0,
        int zugfuehrerCount = 0) =>
        _inner.AddForceUnit(brigade, personnelCount, callSign, status, notes, scbaCount, officerCount, zugfuehrerCount);

    public void UpdateForceUnit(Guid unitId, string? status, string? notes) => _inner.UpdateForceUnit(unitId, status, notes);

    public void UpdateForceStrength(Guid unitId, int officerCount, int personnelCount, int scbaCount, int zugfuehrerCount = 0) =>
        _inner.UpdateForceStrength(unitId, officerCount, personnelCount, scbaCount, zugfuehrerCount);

    public void RemoveForceUnit(Guid unitId) => _inner.RemoveForceUnit(unitId);

    public void AddTask(string text, string? assignee, TaskImportance importance, TaskUrgency urgency, int timerMinutes) =>
        _inner.AddTask(text, assignee, importance, urgency, timerMinutes);

    public void SetTaskCompleted(Guid taskId, bool isDone) => _inner.SetTaskCompleted(taskId, isDone);

    public void UpdateTask(Guid taskId, string text, string? assignee, TaskImportance importance, TaskUrgency urgency) =>
        _inner.UpdateTask(taskId, text, assignee, importance, urgency);

    public void ExtendTaskTimer(Guid taskId, int minutes) => _inner.ExtendTaskTimer(taskId, minutes);

    public void AddScbaTrupp(
        string designation,
        IEnumerable<TruppMember> members,
        int entryPressure,
        int? truppNumber = null,
        string? callSign = null,
        string? task = null,
        int maxDurationMinutes = AtemschutzTrupp.DefaultMaxDurationMinutes,
        int returnPressureBar = AtemschutzTrupp.DefaultReturnPressureBar,
        int pressureControlIntervalMinutes = AtemschutzTrupp.DefaultPressureControlIntervalMinutes) =>
        _inner.AddScbaTrupp(
            designation,
            members,
            entryPressure,
            truppNumber,
            callSign,
            task,
            maxDurationMinutes,
            returnPressureBar,
            pressureControlIntervalMinutes);

    public void StartScbaTrupp(Guid truppId) => _inner.StartScbaTrupp(truppId);

    public void RecordScbaPressure(Guid truppId, int bar) => _inner.RecordScbaPressure(truppId, bar);

    public void WithdrawScbaTrupp(Guid truppId) => _inner.WithdrawScbaTrupp(truppId);

    public void MarkScbaRemoved(Guid truppId) => _inner.MarkScbaRemoved(truppId);

    public void SetIncidentNumber(IncidentNumber? number) => _inner.SetIncidentNumber(number);

    public void SetKeyword(string? keyword) => _inner.SetKeyword(keyword);

    public void SetAddress(string? street, string? district) => _inner.SetAddress(street, district);

    public void SetStatus(string? status) => _inner.SetStatus(status);

    public void UpsertTimer(string key, DateTimeOffset cycleAnchor, int intervalMinutes, int recurringIntervalMinutes, bool isRunning) =>
        _inner.UpsertTimer(key, cycleAnchor, intervalMinutes, recurringIntervalMinutes, isRunning);

    public void Close() => _inner.Close();

    public Task AddFileAsync(string fileName, string contentType, byte[] bytes, CancellationToken cancellationToken = default) =>
        _inner.AddFileAsync(fileName, contentType, bytes, cancellationToken);

    public Task<byte[]?> GetFileBytesAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        _inner.GetFileBytesAsync(fileId, cancellationToken);

    public void RenameFile(Guid fileId, string? displayName) => _inner.RenameFile(fileId, displayName);

    public Task RemoveFileAsync(Guid fileId, CancellationToken cancellationToken = default) =>
        _inner.RemoveFileAsync(fileId, cancellationToken);

    public void AddCoBuilding(string name, int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0) =>
        _inner.AddCoBuilding(name, floorCount, apartmentsPerFloor, undergroundFloorCount);

    public void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0) =>
        _inner.UpdateCoBuildingStructure(buildingId, floorCount, apartmentsPerFloor, undergroundFloorCount);

    public void RemoveCoBuilding(Guid buildingId) => _inner.RemoveCoBuilding(buildingId);

    public void RecordCoValue(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue) =>
        _inner.RecordCoValue(buildingId, floorOrdinal, apartmentNumber, coValue);

    public void SetDwellingStatus(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status) =>
        _inner.SetDwellingStatus(buildingId, floorOrdinal, apartmentNumber, status);

    public void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, bool? keyAvailable) =>
        _inner.SetDwellingDetails(buildingId, floorOrdinal, apartmentNumber, residentName, keyAvailable);

    public void SetFloorDescription(Guid buildingId, int floorOrdinal, string? description) =>
        _inner.SetFloorDescription(buildingId, floorOrdinal, description);

    public void SetApartmentLabel(Guid buildingId, int floorOrdinal, int apartmentNumber, string? label) =>
        _inner.SetApartmentLabel(buildingId, floorOrdinal, apartmentNumber, label);

    public void SetApartmentCount(Guid buildingId, int floorOrdinal, int count) =>
        _inner.SetApartmentCount(buildingId, floorOrdinal, count);
}
