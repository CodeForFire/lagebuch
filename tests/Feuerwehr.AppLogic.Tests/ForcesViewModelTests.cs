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
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(session, new FixedClock(T0), Md(), () => changes++)
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

    [Fact]
    public void Editing_a_row_status_reaches_the_domain_and_persists()
    {
        var changes = 0;
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(session, new FixedClock(T0), Md(), () => changes++)
        {
            NewBrigade = "FFB Wache 1",
            NewPersonnelCount = 9,
            NewStatus = "Alarmiert",
        };
        vm.AddForceCommand.Execute(null);
        changes = 0;

        var row = Assert.Single(vm.Forces);
        row.Status = "Im Einsatz";

        Assert.Equal("Im Einsatz", session.Incident.Forces[0].Status);
        // An edit is a change to the Einsatz record, so it has to trigger the same save the add does.
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Editing_a_row_bemerkung_reaches_the_domain()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(session, new FixedClock(T0), Md(), () => { })
        {
            NewBrigade = "FFB Wache 1",
            NewPersonnelCount = 9,
        };
        vm.AddForceCommand.Execute(null);

        var row = Assert.Single(vm.Forces);
        row.Notes = "über DLK angefordert";

        Assert.Equal("über DLK angefordert", session.Incident.Forces[0].Notes);
    }

    [Fact]
    public void Editing_a_row_leaves_the_rest_of_the_unit_alone()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(session, new FixedClock(T0), Md(), () => { })
        {
            NewBrigade = "FFB Wache 1",
            NewCallSign = "FFB 1/40/1",
            NewPersonnelCount = 9,
            NewScbaCount = 4,
        };
        vm.AddForceCommand.Execute(null);

        var row = Assert.Single(vm.Forces);
        row.Status = "Im Einsatz";

        var unit = session.Incident.Forces[0];
        Assert.Equal("FFB Wache 1", unit.Brigade);
        Assert.Equal("FFB 1/40/1", unit.CallSign);
        Assert.Equal(9, unit.PersonnelCount);
        Assert.Equal(4, unit.ScbaCount);
        // Totals are derived from the units, so they must not drift on a status edit.
        Assert.Equal(9, vm.TotalPersonnel);
        Assert.Equal(4, vm.TotalScba);
    }

    [Fact]
    public void Rows_of_a_readonly_incident_are_not_editable()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        seed.Incident.AddForceUnit(clock, new SessionOperator("Müller"), "FFB Wache 1", 9, null, "Alarmiert");
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new ForcesViewModel(ro, new FixedClock(T0), Md(), () => { });

        var row = Assert.Single(vm.Forces);
        Assert.True(row.IsReadOnly);

        // A closed Einsatz is a historical record. Setting must be inert rather than throwing --
        // the grid binds two-way and would otherwise blow up on a stray edit.
        row.Status = "Im Einsatz";
        Assert.Equal("Alarmiert", ro.Incident.Forces[0].Status);
    }

    [Fact]
    public void Adding_and_restatusing_a_unit_writes_the_etb_entries_as_the_session_operator()
    {
        // Pins that the view model hands the real clock and operator down: the domain guarantees
        // an entry exists, but only this layer decides whose name is on it.
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(session, clock, Md(), () => { })
        {
            NewBrigade = "FFB Wache 1",
            NewCallSign = "FFB 1/40/1",
            NewPersonnelCount = 9,
            NewStatus = "Alarmiert",
        };
        var before = session.Incident.Journal.Count;

        vm.AddForceCommand.Execute(null);
        Assert.Equal(before + 1, session.Incident.Journal.Count);
        Assert.Equal("Müller (FFB 12/1)", session.Incident.Journal[^1].EnteredBy);
        Assert.Equal(T0, session.Incident.Journal[^1].Timestamp);

        vm.Forces[0].Status = "Im Einsatz";
        Assert.Equal(before + 2, session.Incident.Journal.Count);
        Assert.Contains("Status Alarmiert → Im Einsatz", session.Incident.Journal[^1].Text);
    }

    [Fact]
    public void Editing_only_the_bemerkung_adds_no_etb_entry()
    {
        // The grid writes the Bemerkung through on every keystroke, so this is what keeps a typed
        // note from burying the Einsatztagebuch.
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new ForcesViewModel(session, clock, Md(), () => { })
        {
            NewBrigade = "FFB Wache 1",
            NewPersonnelCount = 9,
            NewStatus = "Alarmiert",
        };
        vm.AddForceCommand.Execute(null);
        var after = session.Incident.Journal.Count;

        vm.Forces[0].Notes = "ü";
        vm.Forces[0].Notes = "üb";
        vm.Forces[0].Notes = "übe";

        Assert.Equal(after, session.Incident.Journal.Count);
        Assert.Equal("übe", session.Incident.Forces[0].Notes);
    }


    private static ForcesViewModel NewVm()
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), new FixedClock(T0),
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        return new ForcesViewModel(session, new FixedClock(T0), Md(), () => { });
    }
}
