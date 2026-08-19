using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.AppLogic.Tests;

public class RolesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md(params Person[] personnel) => MasterDataSet.Empty with
    {
        Roles = new[] { "EL", "ZF" },
        RadioCallSigns = new[] { "FFB 12/1" },
        Personnel = personnel,
    };

    private static readonly Person Max = new("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67");

    private static RolesViewModel NewVm(FixedClock clock, MasterDataSet md, Action? onChanged = null)
    {
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        return new RolesViewModel(session, clock, md, onChanged ?? (() => { }));
    }

    [Fact]
    public void AddRole_appends_and_clears_and_fires_onchanged()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new RolesViewModel(session, clock, Md(), () => changes++)
        {
            NewRole = "EL",
            NewPersonName = "Müller",
            NewSection = "Abschnitt Nord",
            NewCallSign = "FFB 12/1",
            NewPhone = "01 71 / 1 11 11 11"
        };

        Assert.Equal(new[] { "EL", "ZF" }, vm.RoleOptions);
        Assert.True(vm.AddRoleCommand.CanExecute(null));
        vm.AddRoleCommand.Execute(null);

        var assignment = Assert.Single(session.Incident.Roles);
        Assert.Equal("Abschnitt Nord", assignment.Section);
        Assert.Equal("01 71 / 1 11 11 11", assignment.Phone);
        Assert.Single(vm.Roles);
        Assert.Equal("", vm.NewPersonName);
        Assert.Null(vm.NewSection);
        Assert.Null(vm.NewPhone);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddRole_disabled_when_role_or_person_blank()
    {
        var vm = NewVm(new FixedClock(T0), Md());
        vm.NewRole = "EL";
        vm.NewPersonName = "";
        Assert.False(vm.AddRoleCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        session.Close();
        var vm = new RolesViewModel(session, clock, Md(), () => { }) { NewRole = "EL", NewPersonName = "Müller" };
        Assert.False(vm.AddRoleCommand.CanExecute(null));
    }

    // --- Abschnitt / von / bis / Handynummer (issue #17) ---

    [Fact]
    public void Von_is_stamped_from_the_clock_when_the_assignment_is_created()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md());
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        var row = Assert.Single(vm.Roles);
        Assert.Equal(T0, row.From);
        Assert.Null(row.To);
        Assert.True(row.IsRunning);
        Assert.Equal("—", row.ToDisplay);
    }

    [Fact]
    public void Ending_an_assignment_stamps_bis_and_persists()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md(), () => changes++);
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);
        changes = 0;

        var row = Assert.Single(vm.Roles);
        clock.Now = T0.AddMinutes(45);
        Assert.True(row.EndCommand.CanExecute(null));
        row.EndCommand.Execute(null);

        // The collection is rebuilt from the aggregate on change, so re-read the row.
        var ended = Assert.Single(vm.Roles);
        Assert.Equal(T0.AddMinutes(45), ended.To);
        Assert.False(ended.IsRunning);
        Assert.NotEqual("—", ended.ToDisplay);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void An_ended_assignment_cannot_be_ended_again()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md());
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        var row = Assert.Single(vm.Roles);
        row.EndCommand.Execute(null);

        // The button hides itself rather than silently overwriting the handover time.
        Assert.False(Assert.Single(vm.Roles).EndCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_disables_ending_an_assignment()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        seed.Incident.AssignRole("EL", "Müller", from: T0);
        seed.Save();

        var vm = new RolesViewModel(LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident"), clock, Md(), () => { });

        var row = Assert.Single(vm.Roles);
        Assert.True(row.IsRunning);
        Assert.False(row.EndCommand.CanExecute(null));
    }

    // --- Personnel roster (issue #17) ---

    [Fact]
    public void Picking_a_known_person_fills_in_phone_and_call_sign()
    {
        var vm = NewVm(new FixedClock(T0), Md(Max));
        Assert.Equal(new[] { "Mustermann, Max" }, vm.PersonOptions);

        vm.NewPersonName = "Mustermann, Max";

        Assert.Equal("01 71 / 1 23 45 67", vm.NewPhone);
        Assert.Equal("Land 1", vm.NewCallSign);
    }

    [Fact]
    public void A_hand_typed_phone_number_outranks_the_roster()
    {
        var vm = NewVm(new FixedClock(T0), Md(Max));
        vm.NewPhone = "01 60 / 9 99 99 99";

        vm.NewPersonName = "Mustermann, Max";

        Assert.Equal("01 60 / 9 99 99 99", vm.NewPhone);
    }

    [Fact]
    public void An_unknown_name_is_accepted_as_free_text()
    {
        var vm = NewVm(new FixedClock(T0), Md(Max));
        vm.NewRole = "EL";
        vm.NewPersonName = "Nachbarwehr, Nicht Im Verzeichnis";

        Assert.True(vm.AddRoleCommand.CanExecute(null));
        vm.AddRoleCommand.Execute(null);

        Assert.Equal("Nachbarwehr, Nicht Im Verzeichnis", Assert.Single(vm.Roles).PersonName);
    }

    [Fact]
    public void An_empty_roster_is_normal_and_leaves_the_name_free_text()
    {
        // personnel.json is gitignored, so a fresh clone and CI both run with no roster at all.
        var vm = NewVm(new FixedClock(T0), Md());
        Assert.Empty(vm.PersonOptions);

        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        Assert.True(vm.AddRoleCommand.CanExecute(null));
    }
}
