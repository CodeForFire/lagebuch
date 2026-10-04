using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using LageBuch.Domain.Involved;
using LageBuch.Domain.Tasks;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class RolesViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    // The two conflict sentences a person reads, parsed once each so the assertions below can state
    // them without CA1863 re-parsing a template per call. The wording itself is not copied here.
    private static readonly CompositeFormat Held =
        CompositeFormat.Parse(ValidationMessages.FunctionAlreadyHeld);

    private static readonly CompositeFormat HeldInSection =
        CompositeFormat.Parse(ValidationMessages.FunctionAlreadyHeldInSection);

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

    // #540: the view refocuses on EntrySubmitted -- FUNKTION after an add, the invalid field after a
    // refusal. A Funktion already held opens the handover panel instead, which owns focus.
    [Fact]
    public void Submitting_reports_whether_the_role_was_added_and_stays_quiet_on_a_handover()
    {
        var clock = new FixedClock(T0);
        using var vm = NewVm(clock, MdWithRoles(RoleUniqueness.UniquePerIncident));
        var outcomes = new List<bool>();
        vm.EntrySubmitted += (_, e) => outcomes.Add(e.Added);

        vm.NewRole = "EL";
        vm.AddRoleCommand.Execute(null); // no name: refused
        vm.NewPersonName = "Müller";
        vm.AddRoleCommand.Execute(null);
        clock.Now = T0.AddMinutes(1);
        vm.NewRole = "EL";
        vm.NewPersonName = "Schmidt";
        vm.AddRoleCommand.Execute(null); // EL is held: the handover panel opens

        Assert.True(vm.IsTransferring);
        Assert.Equal(new[] { false, true }, outcomes);
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
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, Held, "EL", "Müller"),
            vm.ConflictHint);
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

        // The holder's Abschnitt, not the one on screen: the operator typed "Abschnitt Süd" and the
        // holder is in "Abschnitt Nord", so this sentence must not name the wrong person's
        // Abschnitt. Re-keying the branch on NewSection instead of the matched row would leave every
        // other test in this file green and fail here.
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, HeldInSection, "EL", "Abschnitt Nord", "Müller"),
            vm.ConflictHint);
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

        // The rendered sentence, composed from the constant with the arguments the view model
        // passes: the Funktion, the Abschnitt as the app spells it, and its holder. Composed here
        // rather than written out, so the wording of the one sentence a person reads lives in
        // ValidationMessages alone -- a literal would be a second copy to drift. The earlier half of
        // this test is not the place for it: there the typed Abschnitt is "Abschnitt Süd" while the
        // holder is in "Abschnitt Nord", so ConflictingRow is null and no sentence is rendered at
        // all.
        Assert.Equal(
            string.Format(CultureInfo.InvariantCulture, HeldInSection, "EL", "Abschnitt Nord", "Müller"),
            vm.ConflictHint);
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

    // --- A duplicate that arrived from elsewhere (#470) ---
    // The add form refuses a second holder, but it cannot refuse one that is already recorded: an
    // older Einsatzdatei, an imported .fwincident or a joined device on other Stammdaten can all
    // bring one. Seeded through the domain, which is the only way one can arrive.
    private static RolesViewModel VmOverSeededDuplicate(
        RoleUniqueness uniqueness, string? firstSection = null, string? secondSection = null)
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Incident.AssignRole(clock, op, "EL", "Müller", from: T0, section: firstSection);
        session.Incident.AssignRole(clock, op, "EL", "Schmidt", from: T0, section: secondSection);

        return new RolesViewModel(session, clock, MdWithRoles(uniqueness), () => { });
    }

    // The same, over assignments that were not made here and were not normalized on the way in:
    // SnapshotMapper rehydrates a peer's roles with `new RoleAssignment(...)`, so an Abschnitt a
    // peer stored as blanks reaches this device intact, where Incident's private NormalizeSection is
    // the only thing that folds it. The grid's SameSection has to fold it the same way, or the two
    // call sites disagree and the grid marks what the add form would let through.
    private static RolesViewModel VmOverPeerRoles(
        RoleUniqueness uniqueness, string? firstSection, string? secondSection)
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        store.Save("/x.fwincident", Incident.Rehydrate(
            Guid.NewGuid(),
            T0,
            IncidentState.Open,
            null,
            "Brand",
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ChecklistList>(),
            Array.Empty<EtbEntry>(),
            new[]
            {
                new RoleAssignment(Guid.NewGuid(), "EL", "Müller", null, T0, null, firstSection, null),
                new RoleAssignment(Guid.NewGuid(), "EL", "Schmidt", null, T0, null, secondSection, null),
            },
            Array.Empty<ForceUnit>(),
            Array.Empty<AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<IncidentTimerState>(),
            Array.Empty<IncidentFile>(),
            Array.Empty<IncidentTask>(),
            Array.Empty<Building>(),
            Array.Empty<Dwelling>(),
            Array.Empty<InvolvedParty>()));

        var session = LocalIncidentSession.Open(store, clock, "/x.fwincident", new SessionOperator("Müller"));
        return new RolesViewModel(session, clock, MdWithRoles(uniqueness), () => { });
    }

    // Both rows are marked, not one: either could be the one to hand over, and a marker that picked
    // a winner would be naming a culprit in a record of what happened. The first row is the one a
    // single pass would miss, since its partner is only appended after it.
    [Fact]
    public void A_duplicate_already_in_the_incident_is_marked_on_both_rows()
    {
        var vm = VmOverSeededDuplicate(RoleUniqueness.UniquePerIncident, "Abschnitt Nord", "Abschnitt Nord");

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.True(r.IsDuplicate));
    }

    // The guard that keeps the default inert.
    [Fact]
    public void A_duplicate_of_a_multiple_funktion_is_not_marked()
    {
        var vm = VmOverSeededDuplicate(RoleUniqueness.Multiple, "Abschnitt Nord", "Abschnitt Nord");

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.False(r.IsDuplicate));
    }

    // The marker and the add form ask the same question (Ruling 16), so this is where a drift
    // between the two call sites shows: once per Abschnitt the two holders are legitimate, once per
    // Einsatz they are not whatever Abschnitt either of them sits in.
    [Fact]
    public void A_duplicate_across_abschnitte_is_marked_only_when_the_funktion_is_unique_for_the_einsatz()
    {
        var perSection = VmOverSeededDuplicate(RoleUniqueness.UniquePerSection, "Abschnitt Nord", "Abschnitt Süd");
        Assert.All(perSection.Roles, r => Assert.False(r.IsDuplicate));

        var perIncident = VmOverSeededDuplicate(RoleUniqueness.UniquePerIncident, "Abschnitt Nord", "Abschnitt Süd");
        Assert.All(perIncident.Roles, r => Assert.True(r.IsDuplicate));
    }

    // Two Abschnitte that are both absent are the same Abschnitt, exactly as one named and one
    // absent are not. A reader of SameSection would expect two missing sections to share nothing and
    // leave both rows unmarked; under UniquePerSection they are one bucket, so both rows collide.
    // Without a section argument the helper seeds exactly this, and nothing else in the file did.
    [Fact]
    public void Two_holder_of_a_funktion_unique_per_section_collide_without_an_abschnitt()
    {
        var vm = VmOverSeededDuplicate(RoleUniqueness.UniquePerSection);

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.True(r.IsDuplicate));
    }

    // Pins the fold itself rather than only walking it. Two rows of one bucket whose sections are
    // blank, one null and one of nothing but spaces, is the case where a helper that compared the
    // raw strings would put them in different buckets, and the domain, which trims before it
    // compares, would still call them the same Abschnitt.
    [Fact]
    public void A_whitespace_only_abschnitt_from_a_joined_peer_counts_as_no_abschnitt()
    {
        var vm = VmOverPeerRoles(RoleUniqueness.UniquePerSection, "   ", null);

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.True(r.IsDuplicate));
    }

    // Only a running assignment holds a Funktion — the same liveness test FindRunningRoleHolder
    // applies, in both directions: the ended row must not be marked either, or every Übergabe of a
    // unique Funktion would light up its own history.
    [Fact]
    public void An_ended_assignment_does_not_make_its_funktion_a_duplicate()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var handedOver = session.Incident.AssignRole(clock, op, "EL", "Müller", from: T0);
        session.Incident.EndRoleAssignment(handedOver.Id, T0.AddMinutes(10));
        session.Incident.AssignRole(clock, op, "EL", "Schmidt", from: T0.AddMinutes(10));

        var vm = new RolesViewModel(session, clock, MdWithRoles(RoleUniqueness.UniquePerIncident), () => { })
        {
            ShowAllRoles = true, // otherwise the ended row is filtered out of the grid
        };

        Assert.Equal(2, vm.Roles.Count);
        Assert.All(vm.Roles, r => Assert.False(r.IsDuplicate));
    }

    // #294: the grid is reconciled by id rather than rebuilt with Clear()+re-add.
    private static LocalIncidentSession SessionWithTwoRoles(FixedClock clock)
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AssignRole("EL", "Müller", from: clock.Now, phone: "0171 1");
        session.AssignRole("ZF", "Huber", from: clock.Now);
        return session;
    }

    private static Func<int> CountResets(RolesViewModel vm)
    {
        var resets = 0;
        vm.Roles.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };
        return () => resets;
    }

    [Fact]
    public void An_unrelated_change_keeps_the_role_rows_and_raises_no_reset()
    {
        var clock = new FixedClock(T0);
        var session = SessionWithTwoRoles(clock);
        var vm = new RolesViewModel(session, clock, Md(), () => { });
        var rows = vm.Roles.ToArray();
        var resets = CountResets(vm);

        session.AddJournalEntry(EtbDirection.Outgoing, "Lagemeldung");

        Assert.Equal(0, resets());
        Assert.Equal(rows, vm.Roles);
        Assert.Same(rows[0], vm.Roles[0]);
    }

    [Fact]
    public void Toggling_show_all_inserts_the_ended_row_in_place_without_a_reset()
    {
        var clock = new FixedClock(T0);
        var session = SessionWithTwoRoles(clock);
        var vm = new RolesViewModel(session, clock, Md(), () => { });
        session.TransferRole(vm.Roles[0].Id, "Schmid");
        var resets = CountResets(vm);

        Assert.Equal(new[] { "Huber", "Schmid" }, vm.Roles.Select(r => r.PersonName));

        vm.ShowAllRoles = true;
        Assert.Equal(new[] { "Müller", "Huber", "Schmid" }, vm.Roles.Select(r => r.PersonName));

        vm.ShowAllRoles = false;
        Assert.Equal(new[] { "Huber", "Schmid" }, vm.Roles.Select(r => r.PersonName));
        Assert.Equal(0, resets());
    }

    [Fact]
    public void A_handover_ends_the_kept_row_in_place_and_appends_the_successor()
    {
        var clock = new FixedClock(T0);
        var session = SessionWithTwoRoles(clock);
        var vm = new RolesViewModel(session, clock, Md(), () => { })
        {
            ShowAllRoles = true,
        };
        var el = vm.Roles[0];
        var resets = CountResets(vm);

        clock.Now = T0.AddMinutes(30);
        session.TransferRole(el.Id, "Schmid");

        Assert.Equal(0, resets());
        Assert.Same(el, vm.Roles[0]);
        Assert.Equal(T0.AddMinutes(30), el.To);
        Assert.False(el.IsRunning);
        Assert.False(el.BeginTransferCommand.CanExecute(null));
        Assert.Equal("Schmid", vm.Roles[2].PersonName);
    }

    [Fact]
    public void A_phone_edit_made_elsewhere_updates_the_row_without_writing_back()
    {
        var clock = new FixedClock(T0);
        var session = SessionWithTwoRoles(clock);
        var changes = 0;
        var vm = new RolesViewModel(session, clock, Md(), () => changes++);
        var row = vm.Roles[0];
        var sessionChanges = 0;
        session.Changed += () => sessionChanges++;

        session.EditRolePhone(row.Id, "0171 2");

        Assert.Same(row, vm.Roles[0]);
        Assert.Equal(session.Incident.Roles[0].Phone, row.Phone);
        Assert.Equal(1, sessionChanges);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_phone_edit_arriving_in_a_snapshot_updates_the_row_without_writing_back()
    {
        var clock = new FixedClock(T0);
        var local = SessionWithTwoRoles(clock);
        var remote = new SnapshotRoundTrippingSession(local);
        var changes = 0;
        var vm = new RolesViewModel(remote, clock, Md(), () => changes++);
        var row = vm.Roles[0];
        var resets = CountResets(vm);
        var sessionChanges = 0;
        remote.Changed += () => sessionChanges++;

        local.EditRolePhone(row.Id, "0171 2");

        Assert.Same(row, vm.Roles[0]);
        Assert.Equal(remote.Incident.Roles[0].Phone, row.Phone);
        Assert.Equal(0, resets());
        Assert.Equal(1, sessionChanges);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void A_duplicate_mark_clears_on_the_kept_row_once_its_partner_ends()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Müller");
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var first = session.Incident.AssignRole(clock, op, "EL", "Müller", from: T0);
        session.Incident.AssignRole(clock, op, "EL", "Schmidt", from: T0);
        var vm = new RolesViewModel(session, clock, MdWithRoles(RoleUniqueness.UniquePerIncident), () => { });
        var kept = vm.Roles[1];
        Assert.True(kept.IsDuplicate);

        session.Incident.EndRoleAssignment(first.Id, T0.AddMinutes(10));
        session.AddJournalEntry(EtbDirection.Outgoing, "Lagemeldung"); // raises Changed

        Assert.Same(kept, Assert.Single(vm.Roles));
        Assert.False(kept.IsDuplicate);
    }
}
