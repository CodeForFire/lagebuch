using System.Collections.Specialized;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.ValueObjects;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Tests;

public class CoMessprotokollViewModelTests
{
    private static readonly DateTimeOffset ClockBase = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    private static readonly FixedClock Clock = new(ClockBase);

    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm) CreateVm()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var session = TestSession.StartNew(
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

    // --- HAUS ENTFERNEN / HAUS HINZUFÜGEN at the view-model level --------------------------
    [Fact]
    public void RemoveBuilding_AsksFirst_AndConfirmRemovesIt()
    {
        var changes = 0;
        var (session, _) = CreateVm();
        var vm = new CoMessprotokollViewModel(session, Clock, () => changes++);
        Assert.True(vm.RemoveBuildingCommand.CanExecute(null));

        vm.RemoveBuildingCommand.Execute(null);

        Assert.True(vm.IsRemoveBuildingConfirmOpen);
        Assert.Single(session.Incident.Buildings);

        vm.ConfirmRemoveBuildingCommand.Execute(null);

        Assert.False(vm.IsRemoveBuildingConfirmOpen);
        Assert.Empty(session.Incident.Buildings);
        Assert.Equal(1, changes);
        Assert.Empty(vm.BuildingOptions);
        Assert.False(vm.RemoveBuildingCommand.CanExecute(null)); // nothing left to remove
    }

    [Fact]
    public void RemoveBuilding_CancelKeepsTheHaus()
    {
        var (session, vm) = CreateVm();
        vm.RemoveBuildingCommand.Execute(null);

        vm.CancelRemoveBuildingCommand.Execute(null);

        Assert.False(vm.IsRemoveBuildingConfirmOpen);
        Assert.Single(session.Incident.Buildings);
    }

    [Fact]
    public void AddBuilding_OpensTheForm_AndCancelClosesIt()
    {
        var (_, vm) = CreateVm();
        Assert.True(vm.AddBuildingCommand.CanExecute(null));

        vm.AddBuildingCommand.Execute(null);
        Assert.True(vm.IsAddBuildingDialogOpen);

        vm.CancelAddBuildingCommand.Execute(null);
        Assert.False(vm.IsAddBuildingDialogOpen);
    }

    [Fact]
    public void Filter_FlagsFollowTheChosenFilter()
    {
        var (_, vm) = CreateVm();
        Assert.True(vm.IsFilterAll);

        vm.Filter = CoUnitFilter.Open;
        Assert.True(vm.IsFilterOpen);
        Assert.False(vm.IsFilterAll);

        vm.Filter = CoUnitFilter.Affected;
        Assert.True(vm.IsFilterAffected);
        Assert.False(vm.IsFilterOpen);
    }

    [Fact]
    public void ViewModel_BuildsMatrix_FromIncident()
    {
        var (_, vm) = CreateVm();

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

    // --- Issue #702: the Beteiligten found in a Wohnung ----------------------------------------
    [Fact]
    public void Editor_lists_the_beteiligte_found_in_the_wohnung()
    {
        var (session, vm) = CreateVm();
        var haus = session.Incident.Buildings[0].Id;
        var cell = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0];
        var dwelling = session.Incident.Dwellings.Single(d => d.BuildingId == haus && d.FloorOrdinal == 0 && d.ApartmentNumber == cell.ApartmentNumber);
        session.AddInvolvedParty("Erika Beispiel", null, null);
        session.AddInvolvedParty("POK Mustermann", null, null);
        session.SetInvolvedPartyDwelling(session.Incident.InvolvedParties[0].Id, dwelling.Id);

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);

        var editor = vm.Editor;
        Assert.NotNull(editor);
        Assert.True(editor.HasInvolvedParties);
        Assert.Equal(["Erika Beispiel"], editor.InvolvedPartyNames);
    }

    [Fact]
    public void Editor_of_a_wohnung_without_beteiligte_shows_none()
    {
        var (_, vm) = CreateVm();

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);

        var editor = vm.Editor;
        Assert.NotNull(editor);
        Assert.False(editor.HasInvolvedParties);
    }

    // --- Issue #424: the Messreihe -----------------------------------------------------------
    [Fact]
    public void Editor_ShowsTheCommittedMessreihe_NewestFirst()
    {
        // The clock is shared across this class, so a test that advances it resets it first --
        // otherwise the expected times depend on which tests ran before.
        Clock.Now = ClockBase;
        var (session, vm) = CreateVm();
        var haus = session.Incident.Buildings[0].Id;
        var cell = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0];

        session.RecordCoValue(haus, 0, cell.ApartmentNumber, 250);
        Clock.Now = Clock.Now.AddMinutes(27);
        session.RecordCoValue(haus, 0, cell.ApartmentNumber, 120);
        Clock.Now = Clock.Now.AddMinutes(21);
        session.RecordCoValue(haus, 0, cell.ApartmentNumber, 40);

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);

        Assert.True(vm.Editor!.HasReadings);
        Assert.Equal(new[] { "40 ppm", "120 ppm", "250 ppm" }, vm.Editor.Readings.Select(r => r.Value));
        Assert.Equal(new[] { "10:48", "10:27", "10:00" }, vm.Editor.Readings.Select(r => r.Time));
        Assert.All(vm.Editor.Readings, r => Assert.Equal("Test", r.RecordedBy));

        // Each row keeps its own reading's severity (bands: 30 / 200 / 800 ppm), so a value that
        // has since fallen still reads as having been dangerous when it was taken.
        Assert.True(vm.Editor.Readings[2].IsCoDangerous);
        Assert.True(vm.Editor.Readings[1].IsCoElevated);
        Assert.True(vm.Editor.Readings[0].IsCoElevated);
    }

    [Fact]
    public void Editor_ShowsNoMessreihe_ForAnUnmeasuredWohnung()
    {
        var (_, vm) = CreateVm();

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);

        Assert.False(vm.Editor!.HasReadings);
        Assert.Empty(vm.Editor.Readings);
    }

    [Fact]
    public void Editor_ShowsAClearedValueAsGeloescht()
    {
        var (session, vm) = CreateVm();
        var haus = session.Incident.Buildings[0].Id;
        var cell = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0];

        session.RecordCoValue(haus, 0, cell.ApartmentNumber, 120);
        session.RecordCoValue(haus, 0, cell.ApartmentNumber, null);

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);

        Assert.Equal(new[] { "gelöscht", "120 ppm" }, vm.Editor!.Readings.Select(r => r.Value));
    }

    // A keystroke is not a measurement. Typing then cancelling must leave no trace, and the
    // timestamp of the pending value does not even exist yet -- it is minted at FERTIG.
    [Fact]
    public void APendingCoValue_DoesNotEnterTheMessreihe()
    {
        var (session, vm) = CreateVm();
        var haus = session.Incident.Buildings[0].Id;
        var cell = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0];
        session.RecordCoValue(haus, 0, cell.ApartmentNumber, 120);

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);
        vm.Editor!.CoValue = 999;

        Assert.Single(vm.Editor.Readings);
        Assert.Equal("120 ppm", vm.Editor.Readings[0].Value);

        vm.CloseEditorCommand.Execute(null);
        vm.ConfirmDiscardEditorCommand.Execute(null);

        vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].OpenEditorCommand.Execute(null);
        Assert.Single(vm.Editor!.Readings);
    }

    [Fact]
    public void ConfirmingTheEditor_AppendsExactlyOneReading()
    {
        var (session, vm) = CreateVm();
        var cell = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0];

        cell.OpenEditorCommand.Execute(null);
        vm.Editor!.CoValue = 120;
        vm.ConfirmEditorCommand.Execute(null);

        var dwelling = session.Incident.Dwellings.Single(
            d => d.FloorOrdinal == 0 && d.ApartmentNumber == cell.ApartmentNumber);
        Assert.Single(dwelling.Readings);
        Assert.Equal(120, dwelling.Readings[0].Value);
    }

    [Fact]
    public void TileTooltip_CarriesTheSeries_FromTwoReadingsUp()
    {
        Clock.Now = ClockBase;
        var (session, vm) = CreateVm();
        var haus = session.Incident.Buildings[0].Id;
        var apt = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].ApartmentNumber;

        session.RecordCoValue(haus, 0, apt, 120);
        Assert.DoesNotContain(
            "Messreihe",
            vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].TileTooltip,
            StringComparison.Ordinal);

        Clock.Now = Clock.Now.AddMinutes(27);
        session.RecordCoValue(haus, 0, apt, 40);

        var tooltip = vm.MatrixRows.Single(r => r.Ordinal == 0).Cells[0].TileTooltip;
        Assert.Contains("Messreihe: 10:00 120 ppm · 10:27 40 ppm", tooltip, StringComparison.Ordinal);
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
        TestSession.StartNew(
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
    // AddTask, AddEntry...) gates on its required text field being
    // non-empty. ConfirmAddBuildingCommand had no such gate: clicking HINZUFÜGEN with an empty
    // Hausname called straight through to Building.Create, which throws ArgumentException and
    // takes the whole desktop app down (unhandled on the UI thread).
    [Fact]
    public void ConfirmAddBuilding_NamesTheMissingBezeichnung_InsteadOfGoingGrey()
    {
        var (_, vm) = CreateVm();
        var before = vm.BuildingOptions.Count;
        vm.NewBuildingName = string.Empty;

        Assert.True(vm.ConfirmAddBuildingCommand.CanExecute(null)); // the press is the question (#412)
        vm.ConfirmAddBuildingCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.NewBuildingNameError);
        Assert.Equal(before, vm.BuildingOptions.Count);

        vm.NewBuildingName = "Haus B";
        Assert.Null(vm.NewBuildingNameError); // fixed as it is typed
        vm.ConfirmAddBuildingCommand.Execute(null);
        Assert.Equal(before + 1, vm.BuildingOptions.Count);

        vm.NewBuildingName = "   ";
        vm.ConfirmAddBuildingCommand.Execute(null);
        Assert.Equal(ValidationMessages.Required, vm.NewBuildingNameError);
        Assert.Equal(before + 1, vm.BuildingOptions.Count);
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
        var local = TestSession.StartNew(
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
        var local = TestSession.StartNew(
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

    // --- Issue #241: Refresh's own list upkeep must not count as switching Haus --------------
    private static LocalIncidentSession NewSessionWithTwoHouses()
    {
        var local = TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator("Test", null),
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        local.AddCoBuilding("Haus A", 2, 3);
        local.AddCoBuilding("Haus B", 2, 3);
        return local;
    }

    /// <summary>What the real "HAUS" ComboBox does to its two-way bound SelectedItem: a Reset, or
    /// a Replace of the selected item, makes the Selector push null back through the binding.</summary>
    private static void AttachComboBoxStandIn(CoMessprotokollViewModel vm) =>
        vm.BuildingOptions.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset
                || (e.Action == NotifyCollectionChangedAction.Replace
                    && e.OldItems!.Contains(vm.SelectedBuilding)))
            {
                vm.SelectedBuilding = null;
            }
        };

    [Fact]
    public void UnrelatedChange_RaisesNoCollectionChangeOnBuildingOptions()
    {
        var (session, vm) = CreateVm();
        var events = new List<NotifyCollectionChangedAction>();
        vm.BuildingOptions.CollectionChanged += (_, e) => events.Add(e.Action);

        session.AddJournalEntry(EtbDirection.Internal, "Lage unverändert");

        Assert.Empty(events);
    }

    [Fact]
    public void UnrelatedChange_OnAJoinedSession_RaisesNoResetOnBuildingOptions()
    {
        var local = NewSessionWithTwoHouses();
        var vm = new CoMessprotokollViewModel(new SnapshotRoundTrippingSession(local), Clock, () => { });
        var events = new List<NotifyCollectionChangedAction>();
        vm.BuildingOptions.CollectionChanged += (_, e) => events.Add(e.Action);

        local.AddJournalEntry(EtbDirection.Internal, "Lage unverändert");

        // Every instance is fresh after a snapshot round trip, so each is swapped in place.
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, events);
        Assert.Equal(2, vm.BuildingOptions.Count);
    }

    [Fact]
    public void AddingABuilding_StillUpdatesBuildingOptions_AndKeepsTheSelection()
    {
        var local = NewSessionWithTwoHouses();
        var vm = new CoMessprotokollViewModel(local, Clock, () => { });
        vm.SelectedBuilding = vm.BuildingOptions.Single(b => b.Name == "Haus B");

        local.AddCoBuilding("Haus C", 1, 1);

        Assert.Equal(new[] { "Haus A", "Haus B", "Haus C" }, vm.BuildingOptions.Select(b => b.Name));
        Assert.Equal("Haus B", vm.SelectedBuilding?.Name);
    }

    [Fact]
    public void AddingAFloor_SelectsTheFreshBuildingInstance()
    {
        var (_, vm) = CreateVm();
        var rowsBefore = vm.MatrixRows.Count;

        vm.AddObergeschossCommand.Execute(null);

        Assert.Equal(3, vm.SelectedBuilding!.FloorCount);
        Assert.Same(vm.SelectedBuilding, vm.BuildingOptions.Single());
        Assert.Equal(rowsBefore + 1, vm.MatrixRows.Count);
    }

    [Fact]
    public void UnrelatedChange_OnAJoinedSession_KeepsTheOpenEditor_WithABoundComboBox()
    {
        var local = NewSessionWithTwoHouses();
        var vm = new CoMessprotokollViewModel(new SnapshotRoundTrippingSession(local), Clock, () => { });
        vm.SelectedBuilding = vm.BuildingOptions.Single(b => b.Name == "Haus B");
        AttachComboBoxStandIn(vm);

        var editor = OpenEditor(vm, 1, 2);
        editor.CoValue = 45;

        local.AddJournalEntry(EtbDirection.Internal, "Lage unverändert");

        Assert.Same(editor, vm.Editor);
        Assert.Equal(45, vm.Editor!.CoValue);
        Assert.Equal("Haus B", vm.SelectedBuilding?.Name);
        Assert.Equal(45, Cell(vm, 1, 2).CoValue);
    }

    [Fact]
    public void RemovingTheSelectedHaus_ClosesTheEditor()
    {
        var local = NewSessionWithTwoHouses();
        var vm = new CoMessprotokollViewModel(local, Clock, () => { });
        var houseB = vm.BuildingOptions.Single(b => b.Name == "Haus B");
        vm.SelectedBuilding = houseB;
        AttachComboBoxStandIn(vm);
        OpenEditor(vm, 1, 1);

        local.RemoveCoBuilding(houseB.Id);

        Assert.Null(vm.Editor);
        Assert.Equal("Haus A", vm.SelectedBuilding?.Name);
    }

    [Fact]
    public void RemovingTheEditedFloorRemotely_ClosesTheEditor()
    {
        var (session, vm) = CreateVm();
        var building = vm.SelectedBuilding!;
        OpenEditor(vm, 2, 1);

        // Another device takes the top OG away; this VM only sees the resulting Changed.
        session.UpdateCoBuildingStructure(building.Id, 1, building.ApartmentsPerFloor);

        Assert.Null(vm.Editor);
    }

    [Fact]
    public void ViewModel_EmptyState_NoBuildings()
    {
        var op = new SessionOperator("Test", null);
        var store = new FakeStore();
        var session = TestSession.StartNew(
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
        editor.KeyAvailable = KeyAvailability.Available;
        editor.KeyNumber = "14";
        editor.Label = "Hinterhaus";

        vm.CloseEditorCommand.Execute(null);
        vm.ConfirmDiscardEditorCommand.Execute(null); // #652: a changed buffer asks first

        var dwelling = DwellingOf(session, buildingId, 0, 1);
        Assert.Equal(DwellingStatus.NotSearched, dwelling.Status);
        Assert.Null(dwelling.CoValue);
        Assert.Null(dwelling.ResidentName);
        Assert.Equal(KeyAvailability.Unknown, dwelling.KeyAvailable);
        Assert.Null(dwelling.KeyNumber);
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
        vm.ConfirmDiscardEditorCommand.Execute(null);

        Assert.Equal(before, session.Incident.Journal.Count);
    }

    // #652: a typed ppm value that has not been recorded is a gap in the Messreihe, so ABBRECHEN
    // and Esc (bound to the same command) ask before dropping it.
    [Fact]
    public void Cancelling_a_changed_editor_asks_first_and_keeps_it_open()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        var editor = OpenEditor(vm, 0, 1);
        editor.CoValue = 120;

        vm.CloseEditorCommand.Execute(null);

        Assert.True(vm.IsDiscardEditorConfirmOpen);
        Assert.True(vm.IsEditorOpen);
        Assert.Same(editor, vm.Editor);
        Assert.Null(DwellingOf(session, buildingId, 0, 1).CoValue);
    }

    [Fact]
    public void Declining_the_discard_returns_to_the_edit_with_the_value_kept()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);
        editor.CoValue = 120;
        vm.CloseEditorCommand.Execute(null);

        vm.CancelDiscardEditorCommand.Execute(null);

        Assert.False(vm.IsDiscardEditorConfirmOpen);
        Assert.True(vm.IsEditorOpen);
        Assert.Equal(120, vm.Editor!.CoValue);
    }

    [Fact]
    public void Confirming_the_discard_closes_the_editor_without_writing()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        var editor = OpenEditor(vm, 0, 1);
        editor.ResidentName = "Musterfrau";
        vm.CloseEditorCommand.Execute(null);

        vm.ConfirmDiscardEditorCommand.Execute(null);

        Assert.False(vm.IsDiscardEditorConfirmOpen);
        Assert.False(vm.IsEditorOpen);
        Assert.Null(DwellingOf(session, buildingId, 0, 1).ResidentName);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("co")]
    [InlineData("detail")]
    [InlineData("label")]
    public void Every_kind_of_change_counts(string field)
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);
        switch (field)
        {
            case "status": vm.SetEditorStatusAffectedCommand.Execute(null); break;
            case "co": editor.CoValue = 35; break;
            case "detail": editor.KeyAvailable = KeyAvailability.Available; break;
            default: editor.Label = "Hinterhaus"; break;
        }

        vm.CloseEditorCommand.Execute(null);

        Assert.True(vm.IsDiscardEditorConfirmOpen);
        Assert.True(vm.IsEditorOpen);
    }

    // The keyboard flow stays fast: nothing typed, nothing to lose, no question.
    [Fact]
    public void Cancelling_an_unchanged_editor_closes_it_straight_away()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);
        editor.ResidentName = "   "; // whitespace is no change, as FERTIG sees it

        vm.CloseEditorCommand.Execute(null);

        Assert.False(vm.IsDiscardEditorConfirmOpen);
        Assert.False(vm.IsEditorOpen);
    }

    [Fact]
    public void Committing_while_the_question_is_open_closes_it_too()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        var editor = OpenEditor(vm, 0, 1);
        editor.CoValue = 120;
        vm.CloseEditorCommand.Execute(null);

        vm.ConfirmEditorCommand.Execute(null);

        Assert.False(vm.IsDiscardEditorConfirmOpen);
        Assert.False(vm.IsEditorOpen);
        Assert.Equal(120, DwellingOf(session, buildingId, 0, 1).CoValue);
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
        editor.KeyAvailable = KeyAvailability.Available;
        editor.KeyNumber = " 14 ";
        editor.Label = "Hinterhaus";

        vm.ConfirmEditorCommand.Execute(null);

        var dwelling = DwellingOf(session, buildingId, 0, 1);
        Assert.Equal(DwellingStatus.Affected, dwelling.Status);
        Assert.Equal(120, dwelling.CoValue);
        Assert.Equal("Musterfrau", dwelling.ResidentName);
        Assert.Equal(KeyAvailability.Available, dwelling.KeyAvailable);
        Assert.Equal("14", dwelling.KeyNumber);
        Assert.Equal("Hinterhaus", CoMeasurementLabels.ApartmentLabel(session.Incident.Buildings[0], 0, 1));
        Assert.False(vm.IsEditorOpen);
        Assert.Null(vm.Editor);
    }

    [Fact]
    public void A_changed_Schluesselnummer_alone_is_a_change_that_commits()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.SetDwellingDetails(buildingId, 0, 1, null, KeyAvailability.Available, "14");
        var editor = OpenEditor(vm, 0, 1);
        Assert.Equal("14", editor.KeyNumber);
        Assert.False(editor.HasDetailChange);

        editor.KeyNumber = "15";

        Assert.True(editor.HasDetailChange);
        vm.ConfirmEditorCommand.Execute(null);
        Assert.Equal("15", DwellingOf(session, buildingId, 0, 1).KeyNumber);
    }

    // The field is disabled under "Keiner"/"Unbekannt", but what was typed before the radio moved
    // is still in the buffer: it must neither count as a change nor reach the incident.
    [Fact]
    public void A_Schluesselnummer_without_a_key_is_neither_a_change_nor_committed()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.SetDwellingDetails(buildingId, 0, 1, null, KeyAvailability.NotAvailable);
        var editor = OpenEditor(vm, 0, 1);
        Assert.False(editor.IsKeyNumberEnabled);

        editor.KeyNumber = "14";

        Assert.False(editor.HasDetailChange);
        editor.IsKeyAvailable = true;
        Assert.True(editor.IsKeyNumberEnabled);
        editor.IsKeyNotAvailable = true;
        vm.ConfirmEditorCommand.Execute(null);
        Assert.Null(DwellingOf(session, buildingId, 0, 1).KeyNumber);
    }

    [Fact]
    public void The_tile_previews_the_Schluesselnummer_while_it_is_typed()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);
        var tile = vm.MatrixRows.SelectMany(r => r.Cells).First(c => c.FloorOrdinal == 0 && c.ApartmentNumber == 1);

        editor.KeyAvailable = KeyAvailability.Available;
        Assert.Equal("\uD83D\uDD11", tile.KeyDisplay);

        editor.KeyNumber = "B-14";

        Assert.Equal("\uD83D\uDD11 B-14", tile.KeyDisplay);
        Assert.Contains("Schlüssel Nr. B-14", tile.TileTooltip, StringComparison.Ordinal);

        editor.KeyAvailable = KeyAvailability.NotAvailable;
        Assert.Equal("\u2716", tile.KeyDisplay);
        Assert.Contains("Kein Schlüssel", tile.TileTooltip, StringComparison.Ordinal);
    }

    // The CheckBox this replaced could never get back to "unbekannt" once clicked.
    [Fact]
    public void The_key_can_be_set_back_to_unknown_and_that_commits()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.SetDwellingDetails(buildingId, 0, 1, null, KeyAvailability.Available);
        var editor = OpenEditor(vm, 0, 1);
        Assert.True(editor.IsKeyAvailable);

        editor.IsKeyUnknown = true;

        Assert.Equal(KeyAvailability.Unknown, editor.KeyAvailable);
        Assert.False(editor.IsKeyAvailable);
        Assert.True(editor.HasDetailChange);

        vm.ConfirmEditorCommand.Execute(null);

        Assert.Equal(KeyAvailability.Unknown, DwellingOf(session, buildingId, 0, 1).KeyAvailable);
    }

    // The radios bind TwoWay, so the binding engine writes false into the ones being unchecked;
    // that must never move the value.
    [Fact]
    public void Unchecking_a_key_radio_does_not_change_the_key()
    {
        var (_, vm) = CreateVm();
        var editor = OpenEditor(vm, 0, 1);
        editor.IsKeyNotAvailable = true;

        editor.IsKeyNotAvailable = false;
        editor.IsKeyAvailable = false;

        Assert.Equal(KeyAvailability.NotAvailable, editor.KeyAvailable);
        Assert.True(editor.IsKeyNotAvailable);
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
        vm.ConfirmDiscardEditorCommand.Execute(null);

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
        var writable = TestSession.StartNew(
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
    private readonly Func<Incident, Incident> _rewrite;

    // rewrite lets a test turn the received snapshot into a hostile one.
    public SnapshotRoundTrippingSession(LocalIncidentSession inner, Func<Incident, Incident>? rewrite = null)
    {
        _inner = inner;
        _rewrite = rewrite ?? (incident => incident);
        Incident = _rewrite(Roundtrip(_inner.Incident));
        _inner.Changed += () =>
        {
            Incident = _rewrite(Roundtrip(_inner.Incident));
            Changed?.Invoke();
        };
    }

    private static Incident Roundtrip(Incident incident) =>
        SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(SyncJson.Serialize(SnapshotMapper.ToSnapshot(incident))));

    public Incident Incident { get; private set; }

    /// <summary>Delivers <paramref name="incident"/> the way a host broadcast would, so a test can
    /// play a snapshot that lands in two steps.</summary>
    public void Replace(Incident incident)
    {
        Incident = incident;
        Changed?.Invoke();
    }

    public SessionOperator? Operator => _inner.Operator;

    public bool IsReadOnly => _inner.IsReadOnly;

    public bool IsRemote => true;

    public TimeSpan HostClockOffset { get; set; }

    public event Action? Changed;

    public void ChangeOperator(SessionOperator op) => _inner.ChangeOperator(op);

    public Guid AddJournalEntry(EtbDirection direction, string text, string? from = null, string? to = null) =>
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
        string? phone = null,
        Guid? nodeId = null,
        RoleSlot slot = RoleSlot.None) =>
        _inner.AssignRole(role, personName, callSign, from, to, section, phone, nodeId, slot);

    public void TransferRole(Guid assignmentId, string newPersonName, string? newCallSign = null, string? newPhone = null) =>
        _inner.TransferRole(assignmentId, newPersonName, newCallSign, newPhone);

    public void EditRolePhone(Guid assignmentId, string? phone) => _inner.EditRolePhone(assignmentId, phone);

    public void EditRoleAssignment(Guid assignmentId, string personName, string? callSign, string? phone, string? section) =>
        _inner.EditRoleAssignment(assignmentId, personName, callSign, phone, section);

    public void AddCommandNode(
        CommandNodeKind kind,
        string name,
        Guid? parentId = null,
        string? auftrag = null,
        string? standort = null,
        string? funkgruppe = null,
        string? rufname = null,
        string? telefon = null) =>
        _inner.AddCommandNode(kind, name, parentId, auftrag, standort, funkgruppe, rufname, telefon);

    public void UpdateCommandNode(
        Guid nodeId, string name, string? auftrag, string? standort, string? funkgruppe, string? rufname, string? telefon) =>
        _inner.UpdateCommandNode(nodeId, name, auftrag, standort, funkgruppe, rufname, telefon);

    public void DissolveCommandNode(Guid nodeId) => _inner.DissolveCommandNode(nodeId);

    public void MoveCommandNode(Guid nodeId, Guid? parentId, Guid? beforeNodeId) => _inner.MoveCommandNode(nodeId, parentId, beforeNodeId);

    public void MoveForceUnit(Guid unitId, Guid? nodeId) => _inner.MoveForceUnit(unitId, nodeId);

    public void SetIncidentLocation(GeoPoint? location) => _inner.SetIncidentLocation(location);

    public void PlaceForceUnit(Guid unitId, GeoPoint? position) => _inner.PlaceForceUnit(unitId, position);

    public void PlaceRoleAssignment(Guid assignmentId, Guid? nodeId, RoleSlot slot) => _inner.PlaceRoleAssignment(assignmentId, nodeId, slot);

    public void SetFuehrungsstufe(Fuehrungsstufe? stufe) => _inner.SetFuehrungsstufe(stufe);

    public void UpdateLeadershipPost(string? standort, string? rufname, string? telefon, string? funkgruppe) =>
        _inner.UpdateLeadershipPost(standort, rufname, telefon, funkgruppe);

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

    public void AddTask(string text, string? assignee, TaskImportance importance, TaskUrgency urgency, int timerMinutes, Guid? sourceEntryId = null) =>
        _inner.AddTask(text, assignee, importance, urgency, timerMinutes, sourceEntryId);

    public void SetTaskCompleted(Guid taskId, bool isDone) => _inner.SetTaskCompleted(taskId, isDone);

    public void AddInvolvedParty(string name, string? phone, string? notes) => _inner.AddInvolvedParty(name, phone, notes);

    public void UpdateInvolvedParty(Guid partyId, string name, string? phone, string? notes) =>
        _inner.UpdateInvolvedParty(partyId, name, phone, notes);

    public void RemoveInvolvedParty(Guid partyId) => _inner.RemoveInvolvedParty(partyId);

    public void SetInvolvedPartyDwelling(Guid partyId, Guid? dwellingId) => _inner.SetInvolvedPartyDwelling(partyId, dwellingId);

    public void SetInvolvedPartyWhereabouts(Guid partyId, string? whereabouts) => _inner.SetInvolvedPartyWhereabouts(partyId, whereabouts);

    public void AddSituationReport(
        string? reporter, string? location, string? situation, string? measures, string? units, string? requests) =>
        _inner.AddSituationReport(reporter, location, situation, measures, units, requests);

    public void UpdateSituationReport(
        Guid reportId, string? reporter, string? location, string? situation, string? measures, string? units, string? requests) =>
        _inner.UpdateSituationReport(reportId, reporter, location, situation, measures, units, requests);

    public void RemoveSituationReport(Guid reportId) => _inner.RemoveSituationReport(reportId);

    public void SendSituationReport(Guid reportId, string dispatchCentreName) =>
        _inner.SendSituationReport(reportId, dispatchCentreName);

    public void IssueItem(string device, string issuedTo, string? issuedToPhone, string? issuedToDetails, string? remark) =>
        _inner.IssueItem(device, issuedTo, issuedToPhone, issuedToDetails, remark);

    public void UpdateIssuedItem(Guid itemId, string device, string issuedTo, string? issuedToPhone, string? issuedToDetails, string? remark) =>
        _inner.UpdateIssuedItem(itemId, device, issuedTo, issuedToPhone, issuedToDetails, remark);

    public void ReturnIssuedItem(Guid itemId) => _inner.ReturnIssuedItem(itemId);

    public void UndoIssuedItemReturn(Guid itemId) => _inner.UndoIssuedItemReturn(itemId);

    public void RemoveIssuedItem(Guid itemId) => _inner.RemoveIssuedItem(itemId);

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

    public void RecordScbaPressure(Guid truppId, int bar, string? location = null) => _inner.RecordScbaPressure(truppId, bar, location);

    public void WithdrawScbaTrupp(Guid truppId) => _inner.WithdrawScbaTrupp(truppId);

    public void MarkScbaRemoved(Guid truppId) => _inner.MarkScbaRemoved(truppId);

    public void SetScbaSafetyTrupp(Guid truppId, Guid? safetyTruppId) =>
        _inner.SetScbaSafetyTrupp(truppId, safetyTruppId);

    public void SetIncidentNumber(IncidentNumber? number) => _inner.SetIncidentNumber(number);

    public void SetKeyword(string? keyword) => _inner.SetKeyword(keyword);

    public void SetAddress(string? street, string? district, string? houseNumber = null, string? postalCode = null, string? place = null) =>
        _inner.SetAddress(street, district, houseNumber, postalCode, place);

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

    public void AddCoBuilding(
        string name,
        int floorCount,
        int apartmentsPerFloor,
        int undergroundFloorCount = 0,
        IReadOnlyDictionary<string, string?>? apartmentLabels = null) =>
        _inner.AddCoBuilding(name, floorCount, apartmentsPerFloor, undergroundFloorCount, apartmentLabels);

    public void UpdateCoBuildingStructure(Guid buildingId, int floorCount, int apartmentsPerFloor, int undergroundFloorCount = 0) =>
        _inner.UpdateCoBuildingStructure(buildingId, floorCount, apartmentsPerFloor, undergroundFloorCount);

    public void RemoveCoBuilding(Guid buildingId) => _inner.RemoveCoBuilding(buildingId);

    public void RecordCoValue(Guid buildingId, int floorOrdinal, int apartmentNumber, int? coValue) =>
        _inner.RecordCoValue(buildingId, floorOrdinal, apartmentNumber, coValue);

    public void SetDwellingStatus(Guid buildingId, int floorOrdinal, int apartmentNumber, DwellingStatus status) =>
        _inner.SetDwellingStatus(buildingId, floorOrdinal, apartmentNumber, status);

    public void SetDwellingDetails(Guid buildingId, int floorOrdinal, int apartmentNumber, string? residentName, KeyAvailability keyAvailable, string? keyNumber = null) =>
        _inner.SetDwellingDetails(buildingId, floorOrdinal, apartmentNumber, residentName, keyAvailable, keyNumber);

    public void SetFloorDescription(Guid buildingId, int floorOrdinal, string? description) =>
        _inner.SetFloorDescription(buildingId, floorOrdinal, description);

    public void SetApartmentLabel(Guid buildingId, int floorOrdinal, int apartmentNumber, string? label) =>
        _inner.SetApartmentLabel(buildingId, floorOrdinal, apartmentNumber, label);

    public void SetApartmentCount(Guid buildingId, int floorOrdinal, int count) =>
        _inner.SetApartmentCount(buildingId, floorOrdinal, count);

    public void RemoveDwellings(Guid buildingId, int floorOrdinal, IReadOnlyList<int> apartmentNumbers) =>
        _inner.RemoveDwellings(buildingId, floorOrdinal, apartmentNumbers);
}
