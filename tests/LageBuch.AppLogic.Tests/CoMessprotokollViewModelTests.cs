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

        var cell = vm.MatrixRows.SelectMany(r => r.Cells).First(c => c.BuildingId == houseB.Id);
        cell.CoValue = 45; // enters a ppm value, triggering a RecordCoValue round trip

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
        cell.CoValue = 45;

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

    public void SetApartmentLabel(Guid buildingId, int apartmentNumber, string? label) =>
        _inner.SetApartmentLabel(buildingId, apartmentNumber, label);

    public void AddUndergroundUnit(Guid buildingId, int floorOrdinal) =>
        _inner.AddUndergroundUnit(buildingId, floorOrdinal);

    public void RemoveUndergroundUnit(Guid buildingId, int floorOrdinal, int apartmentNumber) =>
        _inner.RemoveUndergroundUnit(buildingId, floorOrdinal, apartmentNumber);
}
