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

    // --- Rolle übertragen (issue #75): replaces the old standalone "beenden" action -- an
    //     assignment now only ends as part of a handover, or automatically when the incident closes. ---

    [Fact]
    public void Transferring_a_role_ends_the_old_assignment_and_starts_a_new_one()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md(), () => changes++);
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);
        changes = 0;

        var row = Assert.Single(vm.Roles);
        Assert.True(row.BeginTransferCommand.CanExecute(null));
        row.BeginTransferCommand.Execute(null);
        Assert.True(vm.IsTransferring);

        clock.Now = T0.AddMinutes(45);
        vm.TransferPersonName = "Schmidt";
        Assert.True(vm.ConfirmTransferCommand.CanExecute(null));
        vm.ConfirmTransferCommand.Execute(null);

        Assert.False(vm.IsTransferring);
        Assert.Equal(1, changes);

        // "Nur aktuell" is the default filter -- the handed-over assignment drops out of view.
        var current = Assert.Single(vm.Roles);
        Assert.Equal("Schmidt", current.PersonName);
        Assert.True(current.IsRunning);

        vm.ShowAllRoles = true;
        Assert.Equal(2, vm.Roles.Count);
        var ended = vm.Roles.Single(r => r.PersonName == "Müller");
        Assert.Equal(T0.AddMinutes(45), ended.To);
        Assert.False(ended.IsRunning);
    }

    [Fact]
    public void Cancelling_a_transfer_leaves_the_assignment_untouched()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md());
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        var row = Assert.Single(vm.Roles);
        row.BeginTransferCommand.Execute(null);
        vm.TransferPersonName = "Schmidt";

        vm.CancelTransferCommand.Execute(null);

        Assert.False(vm.IsTransferring);
        var unchanged = Assert.Single(vm.Roles);
        Assert.Equal("Müller", unchanged.PersonName);
        Assert.True(unchanged.IsRunning);
    }

    [Fact]
    public void An_ended_assignment_cannot_be_transferred_again()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md());
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);
        Assert.Single(vm.Roles).BeginTransferCommand.Execute(null);
        vm.TransferPersonName = "Schmidt";
        vm.ConfirmTransferCommand.Execute(null);

        vm.ShowAllRoles = true;
        var ended = vm.Roles.Single(r => r.PersonName == "Müller");
        Assert.False(ended.BeginTransferCommand.CanExecute(null));
    }

    [Fact]
    public void ReadOnly_disables_transfer()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        seed.Incident.AssignRole(clock, new SessionOperator("Müller"), "EL", "Müller", from: T0);
        seed.Save();

        var vm = new RolesViewModel(LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident"), clock, Md(), () => { });

        var row = Assert.Single(vm.Roles);
        Assert.True(row.IsRunning);
        Assert.False(row.BeginTransferCommand.CanExecute(null));
    }

    // --- Handynummer editieren (issue #75): a live cell, mirroring ForceRow's Status/Notes. ---

    [Fact]
    public void Editing_the_phone_number_inline_writes_through_and_notifies()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new RolesViewModel(session, clock, Md(), () => changes++)
        {
            NewRole = "EL", NewPersonName = "Müller", NewPhone = "0171",
        };
        vm.AddRoleCommand.Execute(null);
        changes = 0;

        var row = Assert.Single(vm.Roles);
        row.Phone = "0172";

        Assert.Equal("0172", Assert.Single(session.Incident.Roles).Phone);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void ReadOnly_ignores_phone_edits()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        seed.Incident.AssignRole(clock, new SessionOperator("Müller"), "EL", "Müller", phone: "0171", from: T0);
        seed.Save();

        var session = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        var vm = new RolesViewModel(session, clock, Md(), () => { });

        Assert.Single(vm.Roles).Phone = "0172";

        Assert.Equal("0171", Assert.Single(session.Incident.Roles).Phone);
    }

    // --- Filter: nur aktuell (default) vs alles (issue #75), mirrors EtbViewModel.HideSystemEntries. ---

    [Fact]
    public void ShowAllRoles_defaults_to_hiding_ended_assignments()
    {
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var ended = session.Incident.AssignRole(clock, new SessionOperator("Müller"), "EL", "Müller", from: T0);
        session.Incident.EndRoleAssignment(ended.Id, T0.AddMinutes(10));
        session.Incident.AssignRole(clock, new SessionOperator("Müller"), "ZF", "Huber", from: T0);

        var vm = new RolesViewModel(session, clock, Md(), () => { });

        Assert.False(vm.ShowAllRoles);
        var visible = Assert.Single(vm.Roles);
        Assert.Equal("Huber", visible.PersonName);

        vm.ShowAllRoles = true;
        Assert.Equal(2, vm.Roles.Count);
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
