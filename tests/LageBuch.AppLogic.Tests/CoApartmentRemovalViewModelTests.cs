using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Tests;

/// <summary>#419: the floor spinner used to delete Wohnungen off the right-hand end the moment it
/// changed. These cover what it does instead — when it still commits silently, when it stops and
/// asks, and what the question survives.</summary>
public class CoApartmentRemovalViewModelTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));

    private static (LocalIncidentSession Session, CoMessprotokollViewModel Vm) CreateVm(
        int floorCount = 2, int apartmentsPerFloor = 4)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            Clock,
            new SessionOperator("Test", null),
            Path.GetTempFileName(),
            Enumerable.Empty<(string, bool)>(),
            Enumerable.Empty<(string, bool)>());
        session.AddCoBuilding("Haus A", floorCount, apartmentsPerFloor);
        var vm = new CoMessprotokollViewModel(session, Clock, () => { })
        {
            IsStructureMode = true,
        };
        return (session, vm);
    }

    private static FloorRowViewModel Row(CoMessprotokollViewModel vm, int ordinal) =>
        vm.MatrixRows.Single(r => r.Ordinal == ordinal);

    [Fact]
    public void ShrinkingPastEmptyWohnungen_CommitsWithoutAsking()
    {
        var (session, vm) = CreateVm();

        Row(vm, 0).ApartmentCount = 2;

        Assert.Null(vm.PendingApartmentRemoval);
        Assert.False(vm.IsApartmentRemovalOpen);
        Assert.Equal(2, session.Incident.Buildings[0].ApartmentsFor(0));
        Assert.Equal(2, session.Incident.Dwellings.Count(d => d.FloorOrdinal == 0));
    }

    [Fact]
    public void Growing_CommitsWithoutAsking()
    {
        var (session, vm) = CreateVm();
        session.SetDwellingDetails(session.Incident.Buildings[0].Id, 0, 4, "Müller", null);

        Row(vm, 0).ApartmentCount = 6;

        Assert.Null(vm.PendingApartmentRemoval);
        Assert.Equal(6, session.Incident.Buildings[0].ApartmentsFor(0));
    }

    [Fact]
    public void ShrinkingPastRecordedWork_AsksAndWritesNothing()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.RecordCoValue(buildingId, 0, 4, 120);
        var before = session.Incident.Dwellings.Count;

        Row(vm, 0).ApartmentCount = 3;

        Assert.NotNull(vm.PendingApartmentRemoval);
        Assert.True(vm.IsApartmentRemovalOpen);
        Assert.Equal(before, session.Incident.Dwellings.Count);
        Assert.Equal(4, session.Incident.Buildings[0].ApartmentsFor(0));
    }

    // The case a Dwelling-only emptiness check misses: a Bezeichnung lives on the Building, so a
    // floor of named shops would otherwise be trimmed away without a word.
    [Fact]
    public void ShrinkingPastAWohnungCarryingOnlyItsBezeichnung_Asks()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.SetApartmentLabel(buildingId, 0, 4, "Kiosk");

        Row(vm, 0).ApartmentCount = 3;

        Assert.NotNull(vm.PendingApartmentRemoval);
        Assert.Equal(4, session.Incident.Buildings[0].ApartmentsFor(0));

        // ...and it reads as carrying something, so the row is flagged rather than shown as a
        // throwaway — even though the Wohnung itself holds no Messwert.
        var kiosk = vm.PendingApartmentRemoval!.Rows.Single(r => r.ApartmentNumber == 4);
        Assert.True(kiosk.CarriesData);
        Assert.Equal("Kiosk", kiosk.Label);
    }

    [Fact]
    public void ThePicker_PreselectsExactlyAsManyAsTheTypedCountImplies()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);

        Row(vm, 0).ApartmentCount = 2;

        var pending = vm.PendingApartmentRemoval!;
        Assert.Equal(2, pending.RequiredCount);
        Assert.Equal(2, pending.SelectedCount);
        Assert.True(pending.CanConfirm);

        // The empty Whg. 3 goes before the measured Whg. 4: the least destructive selection that
        // still reaches the count that was typed.
        Assert.Contains(3, pending.SelectedApartmentNumbers);
    }

    [Fact]
    public void Unticking_BlocksConfirmUntilTheCountMatchesAgain()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);

        Row(vm, 0).ApartmentCount = 3;
        var pending = vm.PendingApartmentRemoval!;
        var ticked = pending.Rows.Single(r => r.IsSelected);
        ticked.IsSelected = false;

        Assert.False(pending.CanConfirm);
        Assert.Equal("0 von 1 ausgewählt", pending.SelectionLabel);

        pending.Rows.Single(r => r.ApartmentNumber == 1).IsSelected = true;
        Assert.True(pending.CanConfirm);
    }

    [Fact]
    public void Confirming_RemovesExactlyTheTickedUnits_AndKeepsTheRestIntact()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.SetDwellingDetails(buildingId, 0, 1, "Müller", null);
        session.RecordCoValue(buildingId, 0, 4, 120);

        Row(vm, 0).ApartmentCount = 3;
        var pending = vm.PendingApartmentRemoval!;
        foreach (var row in pending.Rows)
        {
            row.IsSelected = row.ApartmentNumber == 2;
        }

        vm.ConfirmApartmentRemovalCommand.Execute(null);

        Assert.Null(vm.PendingApartmentRemoval);
        Assert.Equal(3, session.Incident.Buildings[0].ApartmentsFor(0));

        var floor = session.Incident.Dwellings
            .Where(d => d.FloorOrdinal == 0)
            .OrderBy(d => d.ApartmentNumber)
            .ToList();
        Assert.Equal("Müller", floor[0].ResidentName);
        Assert.Equal(120, floor[2].CoValue); // Whg. 4 moved down to 3, value intact
    }

    [Fact]
    public void Cancelling_LeavesTheFloorAloneAndSnapsTheSpinnerBack()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);

        Row(vm, 0).ApartmentCount = 2;
        vm.CancelApartmentRemovalCommand.Execute(null);

        Assert.Null(vm.PendingApartmentRemoval);
        Assert.Equal(4, session.Incident.Buildings[0].ApartmentsFor(0));
        Assert.Equal(4, Row(vm, 0).ApartmentCount);
        Assert.Equal(4, Row(vm, 0).Cells.Count);
    }

    // The spinner is typed, not clicked, so "4" -> "1" -> "13" arrives one keystroke at a time.
    [Fact]
    public void TypingFurther_RetargetsTheQuestion()
    {
        var (session, vm) = CreateVm(apartmentsPerFloor: 6);
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 6, 120);

        Row(vm, 0).ApartmentCount = 1;
        Assert.Equal(5, vm.PendingApartmentRemoval!.RequiredCount);

        Row(vm, 0).ApartmentCount = 4;
        Assert.Equal(4, vm.PendingApartmentRemoval!.TargetCount);
        Assert.Equal(2, vm.PendingApartmentRemoval!.RequiredCount);
    }

    [Fact]
    public void TypingBackToTheCommittedCount_ClosesTheQuestion()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);

        Row(vm, 0).ApartmentCount = 2;
        Assert.NotNull(vm.PendingApartmentRemoval);

        Row(vm, 0).ApartmentCount = 4;

        Assert.Null(vm.PendingApartmentRemoval);
        Assert.Equal(4, session.Incident.Buildings[0].ApartmentsFor(0));
    }

    [Fact]
    public void LeavingStructureMode_DropsTheQuestion()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);
        Row(vm, 0).ApartmentCount = 3;

        vm.IsStructureMode = false;

        Assert.Null(vm.PendingApartmentRemoval);
    }

    [Fact]
    public void SwitchingHaus_DropsTheQuestion()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);
        session.AddCoBuilding("Haus B", 1, 2);
        Row(vm, 0).ApartmentCount = 3;
        Assert.NotNull(vm.PendingApartmentRemoval);

        vm.SelectedBuilding = vm.BuildingOptions.Single(b => b.Name == "Haus B");

        Assert.Null(vm.PendingApartmentRemoval);
    }

    // A push from a joined device can move the very floor the question was asked about, and a
    // picker listing Wohnungen that have since shifted is worse than none.
    [Fact]
    public void AnotherChangeToTheIncident_DropsTheQuestion()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.RecordCoValue(buildingId, 0, 4, 120);
        Row(vm, 0).ApartmentCount = 3;
        Assert.NotNull(vm.PendingApartmentRemoval);

        session.RecordCoValue(buildingId, 1, 1, 40);

        Assert.Null(vm.PendingApartmentRemoval);
    }

    // The sidebar addresses its unit by position, so after a renumber FERTIG would write to a
    // different Wohnung than the one on screen.
    [Fact]
    public void Confirming_ClosesAnOpenEditor()
    {
        var (session, vm) = CreateVm();
        session.RecordCoValue(session.Incident.Buildings[0].Id, 0, 4, 120);

        Row(vm, 0).ApartmentCount = 3;
        Row(vm, 0).Cells[0].OpenEditorCommand.Execute(null);
        Assert.NotNull(vm.Editor);

        vm.ConfirmApartmentRemovalCommand.Execute(null);

        Assert.Null(vm.Editor);
        Assert.False(vm.IsEditorOpen);
    }

    [Fact]
    public void ThePickerDescribesWhatEachWohnungCarries()
    {
        var (session, vm) = CreateVm();
        var buildingId = session.Incident.Buildings[0].Id;
        session.RecordCoValue(buildingId, 0, 4, 120);
        session.SetDwellingDetails(buildingId, 0, 3, "Müller", null);

        Row(vm, 0).ApartmentCount = 1;

        var pending = vm.PendingApartmentRemoval!;
        Assert.Equal("leer", pending.Rows.Single(r => r.ApartmentNumber == 2).Contents);
        Assert.False(pending.Rows.Single(r => r.ApartmentNumber == 2).CarriesData);
        Assert.Contains("Müller", pending.Rows.Single(r => r.ApartmentNumber == 3).Contents, StringComparison.Ordinal);
        Assert.Contains("120 ppm", pending.Rows.Single(r => r.ApartmentNumber == 4).Contents, StringComparison.Ordinal);
        Assert.True(pending.Rows.Single(r => r.ApartmentNumber == 4).CarriesData);
    }
}
