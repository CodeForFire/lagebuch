using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class RolesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md(params Person[] personnel) => MasterDataSet.Empty with
    {
        Roles = new[] { new Role("EL"), new Role("ZF") },
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 12/1", 4) },
        Personnel = personnel,
    };

    // #470: the same catalogue, with the one Funktion under test marked. The rest stays Multiple so
    // an unrelated row can never be what a test trips over.
    private static MasterDataSet MdWithRoles(RoleUniqueness uniqueness, params Person[] personnel) =>
        Md(personnel) with { Roles = new[] { new Role("EL", uniqueness), new Role("ZF") } };

    private static readonly Person Max = new("Mustermann", "Max", "ZF", "Land 1", "01 71 / 1 23 45 67");

    private static RolesViewModel NewVm(FixedClock clock, MasterDataSet md, Action? onChanged = null)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return new RolesViewModel(session, clock, md, onChanged ?? (() => { }));
    }

    [Fact]
    public void AddRole_appends_and_clears_and_fires_onchanged()
    {
        var changes = 0;
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new RolesViewModel(session, clock, Md(), () => changes++)
        {
            NewRole = "EL",
            NewPersonName = "Müller",
            NewSection = "Abschnitt Nord",
            NewCallSign = "FFB 12/1",
            NewPhone = "01 71 / 1 11 11 11",
        };

        Assert.Equal(new[] { "EL", "ZF" }, vm.RoleOptions);
        Assert.True(vm.AddRoleCommand.CanExecute(null));
        vm.AddRoleCommand.Execute(null);

        var assignment = Assert.Single(session.Incident.Roles);
        Assert.Equal("Abschnitt Nord", assignment.Section);
        Assert.Equal("01 71 / 1 11 11 11", assignment.Phone);
        Assert.Single(vm.Roles);
        Assert.Equal(string.Empty, vm.NewPersonName);
        Assert.Null(vm.NewSection);
        Assert.Null(vm.NewPhone);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddRole_names_the_blank_person_and_leaves_the_filled_Funktion_alone()
    {
        var vm = NewVm(new FixedClock(T0), Md());
        vm.NewRole = "EL";
        vm.NewPersonName = string.Empty;

        Assert.True(vm.AddRoleCommand.CanExecute(null)); // the press is the question (#412)
        vm.AddRoleCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.NewPersonNameError);
        Assert.Null(vm.NewRoleError); // only the offending field is named
        Assert.Empty(vm.Roles);

        vm.NewPersonName = "Müller";
        Assert.Null(vm.NewPersonNameError);
    }

    [Fact]
    public void ConfirmTransfer_names_the_blank_successor_and_keeps_the_panel_open()
    {
        var vm = NewVm(new FixedClock(T0), Md());
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);
        Assert.Single(vm.Roles).BeginTransferCommand.Execute(null);

        Assert.True(vm.ConfirmTransferCommand.CanExecute(null));
        vm.ConfirmTransferCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.TransferPersonNameError);
        Assert.True(vm.IsTransferring); // the handover panel stays open on the unfilled name
        Assert.Null(vm.NewPersonNameError); // the dock above it is a different form
    }

    [Fact]
    public void ConfirmTransfer_is_still_impossible_with_no_handover_running()
    {
        var vm = NewVm(new FixedClock(T0), Md());

        // A state gate, not an input one: with no panel open there is no field to name.
        Assert.False(vm.IsTransferring);
        Assert.False(vm.ConfirmTransferCommand.CanExecute(null));
    }

    [Fact]
    public void AddRole_names_a_blank_Funktion_too()
    {
        var vm = NewVm(new FixedClock(T0), Md());
        vm.NewPersonName = "Müller";

        vm.AddRoleCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.NewRoleError);
        Assert.Empty(vm.Roles);
    }

    [Fact]
    public void ReadOnly_disables_add()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
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
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
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
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var vm = new RolesViewModel(session, clock, Md(), () => changes++)
        {
            NewRole = "EL",
            NewPersonName = "Müller",
            NewPhone = "0171",
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
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
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
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
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

    [Fact]
    public void AddRole_adopts_the_stammdaten_spelling_of_a_matching_funktion()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md());
        vm.NewRole = "  el ";
        vm.NewPersonName = "Max Mustermann";

        vm.AddRoleCommand.Execute(null);

        // "el" and "EL " must not become two more Funktionen alongside the configured "EL".
        Assert.Equal("EL", vm.Roles[0].Role);
    }

    [Fact]
    public void AddRole_assigns_an_unknown_funktion_as_typed()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, Md());
        vm.NewRole = "Fachberater THW";
        vm.NewPersonName = "Max Mustermann";

        vm.AddRoleCommand.Execute(null);

        // Free text on purpose: an ad-hoc or mutual-aid role must stay assignable.
        Assert.Equal("Fachberater THW", vm.Roles[0].Role);
    }

    [Fact]
    public void An_unknown_funktion_is_flagged_while_a_configured_one_is_not()
    {
        var vm = NewVm(new FixedClock(T0), Md());

        vm.NewRole = "Fachberater THW";
        Assert.True(vm.IsNewRoleUnknown);

        vm.NewRole = "el";
        Assert.False(vm.IsNewRoleUnknown);

        vm.NewRole = string.Empty;
        Assert.False(vm.IsNewRoleUnknown);
    }

    [Fact]
    public void No_funktion_is_flagged_when_no_stammdaten_are_configured()
    {
        var vm = NewVm(new FixedClock(T0), MasterDataSet.Empty);
        vm.NewRole = "Fachberater THW";

        Assert.False(vm.IsNewRoleUnknown);
    }

    // --- Funktionen, die nur einmal besetzt werden dürfen (issue #470) ---
    // #470: the default must stay inert, or marking one Funktion unique would change how every
    // other one behaves.
    [Fact]
    public void A_funktion_the_stammdaten_leave_multiple_is_never_flagged()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.Multiple));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";
        Assert.Null(vm.ConflictingRow); // while it is typed, so the press below cannot empty the form first

        vm.AddRoleCommand.Execute(null);

        Assert.Equal(2, vm.Roles.Count);
        Assert.Null(vm.ConflictingRow);
    }

    [Fact]
    public void A_funktion_unique_per_einsatz_is_flagged_while_a_holder_runs()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";

        var conflict = vm.ConflictingRow;
        Assert.NotNull(conflict);
        Assert.Equal("Müller", conflict.PersonName);
        Assert.NotNull(vm.ConflictHint);
    }

    // One Einsatzleiter for the whole Einsatz, whichever Abschnitt either of them is typed into --
    // so the Abschnitt the operator picked must not hide a holder sitting in another one.
    [Fact]
    public void A_funktion_unique_per_einsatz_is_flagged_across_abschnitte()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.NewSection = "Abschnitt Nord";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";
        vm.NewSection = "Abschnitt Süd";

        var conflict = vm.ConflictingRow;
        Assert.NotNull(conflict);
        Assert.Equal("Müller", conflict.PersonName);
    }

    [Fact]
    public void A_funktion_unique_per_abschnitt_is_only_flagged_inside_the_same_abschnitt()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerSection));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.NewSection = "Abschnitt Nord";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";
        vm.NewSection = "Abschnitt Süd";
        Assert.Null(vm.ConflictingRow);

        vm.NewSection = "Abschnitt Nord";
        var conflict = vm.ConflictingRow;
        Assert.NotNull(conflict);
        Assert.Equal("Müller", conflict.PersonName);
    }

    // A handover ends the old row and starts a successor for the same Funktion, so the slot is
    // never free afterwards: the conflict clears for Müller and follows Schmidt.
    [Fact]
    public void A_completed_handover_points_the_conflict_at_the_new_holder()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";
        vm.AddRoleCommand.Execute(null); // refused into a hand-off
        vm.ConfirmTransferCommand.Execute(null);

        var conflict = vm.ConflictingRow;
        Assert.NotNull(conflict);
        Assert.Equal("Schmidt", conflict.PersonName);

        var hint = vm.ConflictHint;
        Assert.NotNull(hint);
        Assert.Contains("Schmidt", hint, StringComparison.Ordinal);
        Assert.DoesNotContain("Müller", hint, StringComparison.Ordinal);
    }

    // An unlisted Funktion has no mode, so nothing is refused for it -- the tolerance
    // StammdatenCatalogue exists for.
    [Fact]
    public void A_funktion_absent_from_the_stammdaten_is_never_flagged()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "Fachberater THW";
        vm.NewPersonName = "Schmidt";

        Assert.Null(vm.ConflictingRow);
    }

    [Fact]
    public void A_conflict_is_found_even_when_the_funktion_is_typed_in_another_spelling()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "  el  ";
        vm.NewPersonName = "Schmidt";

        Assert.NotNull(vm.ConflictingRow);
    }

    // A computed property nobody re-raises keeps showing what it was last computed from, so the
    // binding over the add form would show no conflict at all — a property that lies. Both inputs
    // are covered: the typed Abschnitt, and a handover that changes the holder without the form
    // changing by a keystroke.
    [Fact]
    public void The_conflict_is_re_raised_when_the_form_or_the_incident_changes()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerSection));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.NewSection = "Abschnitt Nord";
        vm.AddRoleCommand.Execute(null);

        vm.NewRole = "EL";
        vm.NewPersonName = "Huber";
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.NewSection = "Abschnitt Nord";
        Assert.NotNull(vm.ConflictHint);
        raised.Clear();

        vm.NewSection = "Abschnitt Süd";
        Assert.Null(vm.ConflictHint);
        Assert.Contains(nameof(RolesViewModel.ConflictHint), raised);
        Assert.Contains(nameof(RolesViewModel.ConflictingRow), raised);

        vm.NewSection = "Abschnitt Nord";
        raised.Clear();
        Assert.Single(vm.Roles).BeginTransferCommand.Execute(null);
        vm.TransferPersonName = "Schmidt";
        vm.ConfirmTransferCommand.Execute(null);

        var conflict = vm.ConflictingRow;
        Assert.NotNull(conflict);
        Assert.Equal("Schmidt", conflict.PersonName);
        Assert.Contains(nameof(RolesViewModel.ConflictHint), raised);
        Assert.Contains(nameof(RolesViewModel.ConflictingRow), raised);
    }

    // The refusal is a hand-off, not an error message: the press opens the existing Übergabe panel
    // on the holder, carrying what was already typed, and the add form keeps its values so
    // ABBRECHEN returns to it.
    [Fact]
    public void ZUWEISEN_on_a_held_funktion_opens_the_handover_on_its_holder()
    {
        var clock = new FixedClock(T0);
        var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        vm.NewRole = "EL";
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);

        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";
        vm.NewCallSign = "Florian 2";
        vm.NewPhone = "0171 2";

        vm.AddRoleCommand.Execute(null);

        Assert.Single(vm.Roles);                                     // no second holder was created
        Assert.True(vm.IsTransferring);
        var holder = vm.TransferringRow;
        Assert.NotNull(holder);
        Assert.Equal("EL", holder.Role);
        Assert.Equal("Müller", holder.PersonName);
        Assert.Equal("Schmidt", vm.TransferPersonName);
        Assert.Equal("Florian 2", vm.TransferCallSign);
        Assert.Equal("0171 2", vm.TransferPhone);
        Assert.Equal("EL", vm.NewRole);                            // the add form is left as typed
        Assert.Equal("Schmidt", vm.NewPersonName);
    }
}
