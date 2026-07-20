using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class ForcesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        RadioCallSigns = new[] { "FFB 1/40/1" },
        Brigades = new[] { "FFB Wache 1", "Aich" },
        UnitStatus = new[] { "Alarmiert", "Im Einsatz" },
    };

    [Fact]
    public void AddForce_appends_and_updates_total()
    {
        var changes = 0;
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        var vm = new ForcesViewModel(session, Md(), () => changes++)
        {
            NewBrigade = "FFB",
            NewPersonnelCount = 12
        };

        Assert.True(vm.AddForceCommand.CanExecute(null));
        vm.AddForceCommand.Execute(null);
        vm.NewBrigade = "Emmering";
        vm.NewPersonnelCount = 9;
        vm.AddForceCommand.Execute(null);

        Assert.Equal(2, vm.Forces.Count);
        Assert.Equal(21, vm.TotalPersonnel);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void AddForce_disabled_when_brigade_blank()
    {
        var vm = NewVm();
        vm.NewBrigade = "  ";
        vm.NewPersonnelCount = 5;
        Assert.False(vm.AddForceCommand.CanExecute(null));
    }

    // --- Issue #18 ---

    [Fact]
    public void Brigade_options_come_from_master_data()
    {
        // Was Array.Empty with a "free-text for MVP" comment, so the dropdown was permanently blank.
        Assert.Equal(new[] { "FFB Wache 1", "Aich" }, NewVm().BrigadeOptions);
    }

    [Fact]
    public void Status_options_are_the_per_unit_vocabulary_not_the_incident_one()
    {
        // masterData.Status is aufgenommen/übermittelt/...; a unit is Alarmiert/Im Einsatz/...
        Assert.Equal(new[] { "Alarmiert", "Im Einsatz" }, NewVm().StatusOptions);
    }

    [Fact]
    public void Status_and_notes_reach_the_domain()
    {
        var vm = NewVm();
        vm.NewBrigade = "FFB Wache 1";
        vm.NewPersonnelCount = 9;
        vm.NewStatus = "Im Einsatz";
        vm.NewNotes = "über Drehleiter angefordert";
        vm.AddForceCommand.Execute(null);

        // These were never passed to AddForceUnit, so both columns rendered permanently empty.
        var row = Assert.Single(vm.Forces);
        Assert.Equal("Im Einsatz", row.Status);
        Assert.Equal("über Drehleiter angefordert", row.Notes);
    }

    [Fact]
    public void Scba_count_is_recorded_and_totalled()
    {
        var vm = NewVm();
        vm.NewBrigade = "FFB Wache 1";
        vm.NewPersonnelCount = 9;
        vm.NewScbaCount = 4;
        vm.AddForceCommand.Execute(null);
        vm.NewBrigade = "Aich";
        vm.NewPersonnelCount = 6;
        vm.NewScbaCount = 2;
        vm.AddForceCommand.Execute(null);

        Assert.Equal(15, vm.TotalPersonnel);
        Assert.Equal(6, vm.TotalScba);
        Assert.Equal(new[] { 4, 2 }, vm.Forces.Select(f => f.ScbaCount));
    }

    [Fact]
    public void Add_is_disabled_when_agt_exceed_the_crew()
    {
        var vm = NewVm();
        vm.NewBrigade = "FFB Wache 1";
        vm.NewPersonnelCount = 4;
        vm.NewScbaCount = 5;

        // The button disables rather than letting the click throw out of the domain guard.
        Assert.False(vm.AddForceCommand.CanExecute(null));

        vm.NewScbaCount = 4;
        Assert.True(vm.AddForceCommand.CanExecute(null));
    }

    [Fact]
    public void Inputs_reset_after_adding()
    {
        var vm = NewVm();
        vm.NewBrigade = "FFB Wache 1";
        vm.NewPersonnelCount = 9;
        vm.NewScbaCount = 4;
        vm.NewStatus = "Im Einsatz";
        vm.NewNotes = "Notiz";
        vm.AddForceCommand.Execute(null);

        Assert.Equal("", vm.NewBrigade);
        Assert.Equal(0, vm.NewPersonnelCount);
        Assert.Equal(0, vm.NewScbaCount);
        Assert.Null(vm.NewStatus);
        Assert.Null(vm.NewNotes);
    }

    private static ForcesViewModel NewVm()
    {
        var session = IncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<string>());
        return new ForcesViewModel(session, Md(), () => { });
    }
}
