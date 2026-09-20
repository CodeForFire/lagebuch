using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>
/// The Sicherheitstrupp picker and the ETB lines it writes (#399). The ETB wording is asserted
/// literally: these lines are persisted into the incident file and read back off a printed
/// Einsatzbericht months later, so they are a data format, not incidental display text.
/// </summary>
public class ScbaSafetyTruppTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
        TruppTypes = new[] { new TruppType("Angriffstrupp"), new TruppType("Wassertrupp"), new TruppType("Sicherheitstrupp") },
    };

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static ScbaViewModel Vm(FixedClock clock, LocalIncidentSession session) =>
        new(session, Md(), clock, new FakeTicker(), new FakeAlarmService(), () => { });

    private static ScbaTruppRow Register(ScbaViewModel vm, string designation, string? callSign = null)
    {
        vm.NewDesignation = designation;
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.NewZweiterTruppmann = string.Empty;
        vm.NewCallSign = callSign ?? string.Empty;
        vm.AddTruppCommand.Execute(null);
        return vm.Trupps[^1];
    }

    private static ScbaTruppRow Row(ScbaViewModel vm, Guid id) => vm.Trupps.Single(r => r.Id == id);

    /// <summary>Invokes the flyout entry for <paramref name="safetyId"/>, as a click would.</summary>
    private static void Assign(ScbaViewModel vm, Guid truppId, Guid? safetyId) =>
        Row(vm, truppId).SafetyTruppChoices.Single(c => c.Id == safetyId).SelectCommand.Execute(null);

    /// <summary>The entry the row shows as in force.</summary>
    private static Guid? Current(ScbaTruppRow row) =>
        row.SafetyTruppChoices.Single(c => c.IsCurrent).Id;

    private static List<string> Journal(LocalIncidentSession session) =>
        session.Incident.Journal.Select(e => e.Text).ToList();

    [Fact]
    public void Options_offer_kein_first_then_every_other_waiting_trupp_by_number()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        Register(vm, "Wassertrupp");
        Register(vm, "Sicherheitstrupp");

        var options = Row(vm, angriff.Id).SafetyTruppChoices;

        Assert.Equal(SafetyTruppChoice.NoneDisplay, options[0].Display);
        Assert.Equal(new[] { "— kein —", "Trupp 2", "Trupp 3" }, options.Select(o => o.Display));

        // A Trupp is never offered as its own Sicherheitstrupp.
        Assert.DoesNotContain(options, o => o.Id == angriff.Id);
    }

    [Fact]
    public void A_trupp_under_air_is_not_offered_as_a_standby_crew()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var other = Register(vm, "Wassertrupp");

        Row(vm, other.Id).StartCommand.Execute(null);

        Assert.DoesNotContain(Row(vm, angriff.Id).SafetyTruppChoices, o => o.Id == other.Id);
    }

    [Fact]
    public void The_assigned_standby_stays_in_the_list_after_it_goes_under_air()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, angriff.Id, sicherheit.Id);

        Row(vm, sicherheit.Id).StartCommand.Execute(null);

        // A ComboBox whose SelectedItem is absent from ItemsSource renders blank, which would hide
        // the recorded assignment exactly when it matters most.
        var row = Row(vm, angriff.Id);
        Assert.Contains(row.SafetyTruppChoices, o => o.Id == sicherheit.Id);
        Assert.Equal(sicherheit.Id, Current(row));
    }

    [Fact]
    public void Assigning_writes_the_festgelegt_line()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp", "FFB 1/40/1");
        var sicherheit = Register(vm, "Sicherheitstrupp", "FFB 1/44/1");

        Assign(vm, angriff.Id, sicherheit.Id);

        var entry = session.Incident.Journal.Last();
        Assert.Equal(
            "Sicherheitstrupp für Trupp 1 (Angriffstrupp) festgelegt: Trupp 2 (Sicherheitstrupp)",
            entry.Text);
        Assert.Equal("FFB 1/44/1", entry.From);
        Assert.Equal("FFB 1/40/1", entry.To);
        Assert.Equal(sicherheit.Id, session.Incident.ScbaTrupps[0].SafetyTruppId);
    }

    [Fact]
    public void Changing_writes_the_gewechselt_line_naming_both()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var first = Register(vm, "Sicherheitstrupp");
        var relief = Register(vm, "Wassertrupp");

        Assign(vm, angriff.Id, first.Id);
        Assign(vm, angriff.Id, relief.Id);

        Assert.Equal(
            "Sicherheitstrupp für Trupp 1 (Angriffstrupp) gewechselt: bisher Trupp 2 (Sicherheitstrupp), jetzt Trupp 3 (Wassertrupp)",
            session.Incident.Journal.Last().Text);
    }

    [Fact]
    public void Clearing_writes_the_aufgehoben_line()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");

        Assign(vm, angriff.Id, sicherheit.Id);
        Assign(vm, angriff.Id, null);

        Assert.Equal(
            "Sicherheitstrupp für Trupp 1 (Angriffstrupp) aufgehoben: bisher Trupp 2 (Sicherheitstrupp)",
            session.Incident.Journal.Last().Text);
        Assert.Null(session.Incident.ScbaTrupps[0].SafetyTruppId);
    }

    [Fact]
    public void Starting_names_the_safety_trupp_on_the_im_Einsatz_line()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, angriff.Id, sicherheit.Id);

        Row(vm, angriff.Id).StartCommand.Execute(null);

        Assert.Contains(
            "Trupp 1 (Angriffstrupp) im Einsatz, Sicherheitstrupp: Trupp 2 (Sicherheitstrupp)",
            Journal(session));
    }

    [Fact]
    public void Starting_without_a_safety_trupp_records_the_absence()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");

        Row(vm, angriff.Id).StartCommand.Execute(null);

        // The whole point of #399: the Einsatzbericht has to be able to answer "was anyone standing
        // by?" with something other than silence.
        Assert.Contains("Trupp 1 (Angriffstrupp) im Einsatz, ohne Sicherheitstrupp", Journal(session));
    }

    [Fact]
    public void A_standby_going_under_air_reports_the_loss_for_every_trupp_it_covered()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var first = Register(vm, "Angriffstrupp");
        var second = Register(vm, "Wassertrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, first.Id, sicherheit.Id);
        Assign(vm, second.Id, sicherheit.Id);
        Row(vm, first.Id).StartCommand.Execute(null);
        Row(vm, second.Id).StartCommand.Execute(null);

        Row(vm, sicherheit.Id).StartCommand.Execute(null);

        var journal = Journal(session);
        Assert.Contains(
            "Trupp 3 (Sicherheitstrupp) geht selbst unter Atemschutz — Trupp 1 (Angriffstrupp) ist ohne Sicherheitstrupp",
            journal);
        Assert.Contains(
            "Trupp 3 (Sicherheitstrupp) geht selbst unter Atemschutz — Trupp 2 (Wassertrupp) ist ohne Sicherheitstrupp",
            journal);
    }

    [Fact]
    public void An_abgenommen_trupp_is_not_reported_as_losing_its_cover()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, angriff.Id, sicherheit.Id);
        Row(vm, angriff.Id).StartCommand.Execute(null);
        Row(vm, angriff.Id).WithdrawCommand.Execute(null);
        Row(vm, angriff.Id).MarkRemovedCommand.Execute(null);

        Row(vm, sicherheit.Id).StartCommand.Execute(null);

        Assert.DoesNotContain(Journal(session), t => t.Contains("geht selbst unter Atemschutz", StringComparison.Ordinal));
    }

    [Fact]
    public void Assigning_does_not_tear_the_row_down_underneath_the_picker()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        var row = Row(vm, angriff.Id);

        Assign(vm, angriff.Id, sicherheit.Id);

        // The picker writes to the domain from inside its own selection event. If that write
        // replaces the row object, Avalonia tears the ComboBox down mid-selection and its popup —
        // a real top-level window holding the pointer grab — is orphaned on screen, which freezes
        // the app (#294's Clear()+re-add, made fatal by an interactive control in the row).
        Assert.Same(row, Row(vm, angriff.Id));
        Assert.Equal(sicherheit.Id, Current(row));
    }

    [Fact]
    public void A_row_survives_an_unrelated_change_and_follows_the_domain()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        var row = Row(vm, angriff.Id);

        // A change from anywhere in the incident (here: another device assigning) must update the
        // existing row rather than replace it.
        session.SetScbaSafetyTrupp(angriff.Id, sicherheit.Id);

        Assert.Same(row, Row(vm, angriff.Id));
        Assert.Equal(sicherheit.Id, Current(row));
        Assert.Equal(sicherheit.Id, row.SafetyTruppChoices.Single(o => o.Id == sicherheit.Id).Id);
    }

    [Fact]
    public void A_newly_added_trupp_appears_without_replacing_the_existing_rows()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var first = Row(vm, angriff.Id);

        var sicherheit = Register(vm, "Sicherheitstrupp");

        Assert.Same(first, Row(vm, angriff.Id));
        Assert.Equal(2, vm.Trupps.Count);
        Assert.Equal(sicherheit.Id, vm.Trupps[1].Id);

        // The new Trupp is bereitgestellt, so it must now be offered to the first row.
        Assert.Contains(first.SafetyTruppChoices, o => o.Id == sicherheit.Id);
    }

    [Fact]
    public void The_assignment_survives_the_changes_it_sets_off()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        Row(vm, angriff.Id).StartCommand.Execute(null);
        var wasser = Register(vm, "Wassertrupp");
        Row(vm, wasser.Id).StartCommand.Execute(null);
        var sicherheit = Register(vm, "Sicherheitstrupp");

        Assign(vm, wasser.Id, sicherheit.Id);

        // Assigning writes an ETB entry, which raises Changed again and re-enters the refresh. If
        // that refresh hands the ComboBox a new options list, Avalonia resets SelectedItem and
        // writes "— kein —" back — indistinguishable from the operator picking it, so the
        // assignment was wiped a moment after it was made.
        Assert.Equal(sicherheit.Id, session.Incident.ScbaTrupps.Single(t => t.Id == wasser.Id).SafetyTruppId);
        Assert.Equal(sicherheit.Id, Current(Row(vm, wasser.Id)));

        // And it must survive any later change from anywhere in the incident.
        session.AddJournalEntry(EtbDirection.System, "irgendetwas anderes");
        Assert.Equal(sicherheit.Id, session.Incident.ScbaTrupps.Single(t => t.Id == wasser.Id).SafetyTruppId);
        Assert.Equal(sicherheit.Id, Current(Row(vm, wasser.Id)));
    }

    [Fact]
    public void Re_choosing_the_entry_already_in_force_writes_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, angriff.Id, sicherheit.Id);
        var before = session.Incident.Journal.Count;

        // The flyout offers every entry including the current one, so picking it again is an
        // ordinary click and must not litter the ETB with a second "festgelegt" line.
        Assign(vm, angriff.Id, sicherheit.Id);

        Assert.Equal(before, session.Incident.Journal.Count);
        Assert.Equal(sicherheit.Id, Current(Row(vm, angriff.Id)));
    }

    [Fact]
    public void One_assignment_writes_exactly_one_journal_entry()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");

        Assign(vm, angriff.Id, sicherheit.Id);

        // Every mutation rebuilds every row, and each rebuild re-seeds the ComboBox selection. If
        // that re-seed reached the setter it would re-send the assignment and double the ETB line.
        Assert.Single(Journal(session), t => t.StartsWith("Sicherheitstrupp für", StringComparison.Ordinal));
    }

    [Fact]
    public void Re_selecting_the_value_already_on_the_trupp_changes_nothing()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, angriff.Id, sicherheit.Id);
        var before = session.Incident.Journal.Count;

        Assign(vm, angriff.Id, sicherheit.Id);

        Assert.Equal(before, session.Incident.Journal.Count);
    }

    [Fact]
    public void Hint_warns_when_a_trupp_is_under_air_with_nobody_standing_by()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");

        Assert.Null(Row(vm, angriff.Id).SafetyTruppHint);

        Row(vm, angriff.Id).StartCommand.Execute(null);

        var row = Row(vm, angriff.Id);
        Assert.Equal("kein Sicherheitstrupp", row.SafetyTruppHint);
        Assert.True(row.HasSafetyTruppHint);
    }

    [Fact]
    public void Hint_warns_once_the_standby_crew_has_left_bereitstellung()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        Assign(vm, angriff.Id, sicherheit.Id);

        Row(vm, sicherheit.Id).StartCommand.Execute(null);

        Assert.Equal("nicht mehr bereitgestellt", Row(vm, angriff.Id).SafetyTruppHint);
    }

    [Fact]
    public void Hint_names_the_other_trupps_a_shared_standby_crew_covers()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var first = Register(vm, "Angriffstrupp");
        var second = Register(vm, "Wassertrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");

        Assign(vm, first.Id, sicherheit.Id);
        Assert.Null(Row(vm, first.Id).SafetyTruppHint);

        Assign(vm, second.Id, sicherheit.Id);

        // Shared cover is allowed and merely flagged — the Einsatzleiter decides, the app records.
        Assert.Equal("deckt auch Trupp 2", Row(vm, first.Id).SafetyTruppHint);
        Assert.Equal("deckt auch Trupp 1", Row(vm, second.Id).SafetyTruppHint);
    }

    [Fact]
    public void A_read_only_workspace_cannot_assign()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var angriff = Register(vm, "Angriffstrupp");
        var sicherheit = Register(vm, "Sicherheitstrupp");
        session.Close();

        var readOnly = Vm(clock, session);
        var row = readOnly.Trupps.Single(r => r.Id == angriff.Id);
        Assert.False(row.CanAssignSafetyTrupp);

        var before = session.Incident.Journal.Count;
        row.SafetyTruppChoices.Single(c => c.Id == sicherheit.Id).SelectCommand.Execute(null);

        Assert.Null(session.Incident.ScbaTrupps[0].SafetyTruppId);
        Assert.Equal(before, session.Incident.Journal.Count);
    }
}
