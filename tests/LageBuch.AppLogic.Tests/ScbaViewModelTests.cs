using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class ScbaViewModelTests
{
    /// <summary>A Trupp-Typ the Stammdaten crew with three. The name is incidental since #398 --
    /// what makes it three people is its row, and these tests prove exactly that.</summary>
    private const string CsaTrupp = "CSA-Trupp";

    private const string LpaTrupp = "LPA-Trupp";

    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        Vehicles = new[] { new Vehicle("FFB Wache 1", "FFB 1/40/1", 9) },
        TruppTypes = new[]
        {
            new TruppType("Angriffstrupp"),
            new TruppType("Wassertrupp"),

            // Three people, per its Stammdaten row -- not because the app knows the word "CSA".
            new TruppType(CsaTrupp, 3, 20),

            // #418: also three, but the full 30 minutes, and nothing in its name says "CSA".
            new TruppType("Strahlenschutztrupp", 3, 30),
        },
    };

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    private static ScbaViewModel Vm(FixedClock clock, LocalIncidentSession session, Action? onChanged = null, FakeTicker? ticker = null, FakeAlarmService? alarm = null) =>
        new(session, Md(), clock, ticker ?? new FakeTicker(), alarm ?? new FakeAlarmService(), onChanged ?? (() => { }));

    private static ScbaTruppRow Register(
        ScbaViewModel vm,
        string designation = "Angriffstrupp",
        string truppfuehrer = "Müller",
        string truppmann = "Schmidt",
        string? zweiterTruppmann = null,
        string? callSign = null)
    {
        vm.NewDesignation = designation;
        vm.NewTruppfuehrer = truppfuehrer;
        vm.NewTruppmann = truppmann;
        vm.NewZweiterTruppmann = zweiterTruppmann ?? string.Empty;
        vm.NewCallSign = callSign ?? string.Empty;
        vm.AddTruppCommand.Execute(null);
        return vm.Trupps[^1];
    }

    [Fact]
    public void AddTrupp_registers_a_waiting_trupp_and_does_not_start_the_clock()
    {
        var clock = new FixedClock(T0);
        var changes = 0;
        var session = NewSession(clock);
        var vm = Vm(clock, session, () => changes++);

        var row = Register(vm);

        Assert.Single(session.Incident.ScbaTrupps);
        Assert.True(row.IsWaiting);
        Assert.False(row.IsActive);
        Assert.Equal("Bereitgestellt", row.StatusDisplay);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("bereitgestellt", StringComparison.Ordinal));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddTrupp_names_the_blank_Truppfuehrer()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "  ";
        vm.NewTruppmann = "Schmidt";

        Assert.True(vm.AddTruppCommand.CanExecute(null)); // the press is the question (#412)
        vm.AddTruppCommand.Execute(null);

        Assert.Equal(ValidationMessages.Required, vm.NewTruppfuehrerError);
        Assert.Null(vm.NewTruppmannError); // only the offending field is named
        Assert.Null(vm.NewDesignationError);
        Assert.Empty(session.Incident.ScbaTrupps);
    }

    [Fact]
    public void AddTrupp_names_an_emptied_Einstiegsdruck()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";

        vm.NewEntryPressure = 0;
        vm.AddTruppCommand.Execute(null);

        Assert.Equal(ValidationMessages.EntryPressure, vm.NewEntryPressureError);
        Assert.Empty(session.Incident.ScbaTrupps);

        vm.NewEntryPressure = 300;
        Assert.Null(vm.NewEntryPressureError);
        vm.AddTruppCommand.Execute(null);
        Assert.Single(session.Incident.ScbaTrupps);
    }

    [Fact]
    public void Start_sends_trupp_under_air_and_logs_etb()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm);

        Assert.False(row.RecordPressureCommand.CanExecute(null)); // can't record before start
        Assert.True(row.StartCommand.CanExecute(null));

        clock.Now = T0.AddMinutes(6);
        row.StartCommand.Execute(null);

        Assert.True(row.IsActive);
        Assert.False(row.IsWaiting);
        Assert.False(row.StartCommand.CanExecute(null));
        Assert.True(row.RecordPressureCommand.CanExecute(null));
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("im Einsatz", StringComparison.Ordinal));
    }

    [Fact]
    public void Recording_pressure_logs_entry_and_low_reading_trips_alarm()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm);
        row.StartCommand.Execute(null);

        row.PressureInput = 45;
        row.RecordPressureCommand.Execute(null);

        Assert.True(row.IsAlarm);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Druckkontrolle", StringComparison.Ordinal) && e.Text.Contains("45 bar", StringComparison.Ordinal));
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Rückzugsalarm", StringComparison.Ordinal));
    }

    [Fact]
    public void Header_reminder_tracks_the_soonest_due_active_trupp()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);

        Assert.False(vm.HasControlReminder); // nothing under air yet

        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.StartCommand.Execute(null);

        Assert.True(vm.HasControlReminder);
        Assert.False(vm.IsAnyControlDue);
        Assert.Contains("Nächste Druckabfrage", vm.NextControlDisplay, StringComparison.Ordinal);
    }

    // #417: "Fahrzeug fehlt in der Beschreibung des Trupps - man kann sonst nicht erkennen welcher
    // Trupp da gerade gerufen werden soll." The banner is visible from every tab and names only the
    // most urgent Trupp, so the Funkrufname leads: it is the part that tells two crews apart.
    [Fact]
    public void Header_reminder_leads_with_the_funkrufname()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        var vm = Vm(clock, session, ticker: ticker);
        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm, callSign: "Florian Musterstadt 40/1");
        row.StartCommand.Execute(null);

        Assert.Equal(
            "Nächste Druckabfrage: Florian Musterstadt 40/1 · Trupp 1 (Angriffstrupp) in "
            + row.ControlRemainingDisplay,
            vm.NextControlDisplay);

        clock.Now = T0.AddMinutes(6);
        ticker.Fire();

        Assert.Equal(
            "Druckabfrage fällig: Florian Musterstadt 40/1 · Trupp 1 (Angriffstrupp)",
            vm.NextControlDisplay);
    }

    // The Funkrufname is optional, so a brigade that types none still gets the old wording rather
    // than a dangling separator.
    [Fact]
    public void Header_reminder_falls_back_to_the_plain_name_without_a_funkrufname()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.StartCommand.Execute(null);

        Assert.Equal(
            "Nächste Druckabfrage: Trupp 1 (Angriffstrupp) in " + row.ControlRemainingDisplay,
            vm.NextControlDisplay);
    }

    [Fact]
    public void Tick_when_control_is_due_marks_due_in_header()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        var vm = Vm(clock, session, ticker: ticker);
        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(6); // past the 5-minute control interval
        ticker.Fire();

        Assert.True(row.IsControlDue);
        Assert.True(vm.IsAnyControlDue);
        Assert.Contains("fällig", vm.NextControlDisplay, StringComparison.Ordinal);
    }

    [Fact]
    public void Tick_past_max_duration_logs_exactly_one_alarm_entry()
    {
        var clock = new FixedClock(T0);
        var changes = 0;
        var ticker = new FakeTicker();
        var session = NewSession(clock);
        var vm = Vm(clock, session, () => changes++, ticker);
        vm.NewMaxDurationMinutes = 30;
        var row = Register(vm);
        row.StartCommand.Execute(null);
        var baseline = changes;

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();
        ticker.Fire(); // must not log a second alarm for the same trupp

        var alarms = session.Incident.Journal.Where(e => e.Text.Contains("Rückzugsalarm", StringComparison.Ordinal)).ToList();
        Assert.Single(alarms);
        Assert.Equal(baseline + 1, changes);
    }

    [Fact]
    public void Withdraw_then_MarkRemoved_logs_entries_and_stops_the_clock()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = Register(vm);
        row.StartCommand.Execute(null);

        Assert.True(row.WithdrawCommand.CanExecute(null));
        Assert.False(row.MarkRemovedCommand.CanExecute(null)); // abgenommen not reachable before Rückzug

        clock.Now = T0.AddMinutes(10);
        row.WithdrawCommand.Execute(null);

        Assert.True(row.IsWithdrawing);
        Assert.False(row.IsActive);
        Assert.Equal("Rückzug", row.StatusDisplay);
        Assert.False(row.WithdrawCommand.CanExecute(null));
        Assert.True(row.MarkRemovedCommand.CanExecute(null));
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Rückzug", StringComparison.Ordinal));

        clock.Now = T0.AddMinutes(12);
        row.MarkRemovedCommand.Execute(null);

        Assert.True(row.IsReturned);
        Assert.Equal("—", row.RemainingDisplay);
        Assert.False(vm.HasControlReminder);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("abgenommen", StringComparison.Ordinal));
    }

    [Fact]
    public void Readonly_session_disables_actions()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        seed.Close();
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        var vm = Vm(clock, ro);
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        Assert.True(vm.IsReadOnly);
        Assert.False(vm.AddTruppCommand.CanExecute(null));
        Assert.False(vm.HasControlReminder);
    }

    [Fact]
    public void Dispose_unsubscribes_from_ticker()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var vm = Vm(clock, NewSession(clock), ticker: ticker);
        Assert.Equal(1, ticker.SubscriberCount);

        vm.Dispose();

        Assert.Equal(0, ticker.SubscriberCount);
    }

    [Fact]
    public void Tick_past_max_duration_sounds_the_audible_alarm_and_sets_banner()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var row = Register(vm);
        row.StartCommand.Execute(null);

        Assert.False(vm.IsAnyAlarm);
        Assert.Empty(alarm.Played);

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();

        Assert.True(vm.IsAnyAlarm);
        Assert.Contains(AlarmSound.RetreatAlarm, alarm.Played);

        // Says which Trupp and why, not merely that something is alarming. This fixture's Trupp has
        // no Funkrufname, so the detail falls back to the Trupp name alone -- when one is set it
        // leads, because two vehicles each send a "Trupp 1" (#417).
        var spoken = alarm.Details[alarm.Played.IndexOf(AlarmSound.RetreatAlarm)];
        Assert.StartsWith("Trupp 1", spoken, StringComparison.Ordinal);
        Assert.Contains("erreicht", spoken, StringComparison.Ordinal);
        Assert.Contains("RÜCKZUGSALARM", vm.AlarmDisplay, StringComparison.Ordinal);
        Assert.Contains("Trupp 1 (Angriffstrupp)", vm.AlarmDisplay, StringComparison.Ordinal);
        Assert.True(vm.AcknowledgeAlarmCommand.CanExecute(null));
    }

    // The case from #417: two vehicles each send a "Trupp 1". The Rückzugsalarm names one of them,
    // and without the Funkrufname nothing on screen says which crew to call back.
    [Fact]
    public void Alarm_banner_leads_with_the_funkrufname()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var vm = Vm(clock, NewSession(clock), ticker: ticker);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var row = Register(vm, callSign: "Florian Musterstadt 40/1");
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();

        Assert.Equal(
            "RÜCKZUGSALARM Florian Musterstadt 40/1 · Trupp 1 (Angriffstrupp): Einsatzzeit erreicht",
            vm.AlarmDisplay);
        Assert.StartsWith(
            "RÜCKZUGSALARM Florian Musterstadt 40/1",
            vm.AlarmDisplay,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Acknowledging_alarm_silences_the_repeat_but_keeps_banner()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var row = Register(vm);
        row.StartCommand.Execute(null);
        clock.Now = T0.AddMinutes(31);
        ticker.Fire();
        Assert.Single(alarm.Played);

        vm.AcknowledgeAlarmCommand.Execute(null);

        Assert.True(vm.IsAnyAlarm); // banner remains while the alarm condition persists

        // A further tick with the alarm acknowledged must not re-announce.
        ticker.Fire();
        Assert.Single(alarm.Played);
    }

    [Fact]
    public void Repeat_cadence_is_fifteen_seconds_while_unacknowledged()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var row = Register(vm);
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(30);
        ticker.Fire();
        Assert.Single(alarm.Played);

        clock.Now = T0.AddMinutes(30).AddSeconds(10); // still inside the 15s window
        ticker.Fire();
        Assert.Single(alarm.Played);

        clock.Now = T0.AddMinutes(30).AddSeconds(16); // past the 15s window
        ticker.Fire();
        Assert.Equal(2, alarm.Played.Count);
    }

    [Fact]
    public void Alarm_and_control_reminder_persist_through_Rueckzug_and_clear_on_Abgenommen()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var row = Register(vm);
        row.StartCommand.Execute(null);
        clock.Now = T0.AddMinutes(31);
        ticker.Fire();
        Assert.Single(alarm.Played);

        // Rückzug alone does not silence the alarm -- the crew is still consuming air.
        row.WithdrawCommand.Execute(null);
        Assert.True(vm.IsAnyAlarm);

        var countBeforeRemoval = alarm.Played.Count;
        row.MarkRemovedCommand.Execute(null);

        Assert.False(vm.IsAnyAlarm);
        Assert.Equal(countBeforeRemoval, alarm.Played.Count); // returning does not itself announce

        ticker.Fire();
        Assert.Equal(countBeforeRemoval, alarm.Played.Count); // and no further ticks announce once cleared
    }

    [Fact]
    public void A_second_trupp_newly_alarming_re_announces_after_ack()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);

        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var first = Register(vm);
        first.StartCommand.Execute(null);

        // Second trupp goes under air 10 minutes later, so its limit falls after the first's.
        clock.Now = T0.AddMinutes(10);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999;
        var second = Register(vm, truppfuehrer: "Huber", truppmann: "Mayer");
        second.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();              // first trupp alarms (limit at T0+30)
        Assert.Single(alarm.Played);
        vm.AcknowledgeAlarmCommand.Execute(null);

        clock.Now = T0.AddMinutes(41);
        ticker.Fire();              // second trupp now past its limit (T0+40) → re-announce

        Assert.Equal(2, alarm.Played.Count);
        Assert.All(alarm.Played, s => Assert.Equal(AlarmSound.RetreatAlarm, s));
    }

    [Fact]
    public void A_new_trupp_alarming_after_ack_reannounces_immediately_not_after_the_full_window()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);

        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999; // keep Druckabfrage out of this Rückzugsalarm-only test
        var first = Register(vm);
        first.StartCommand.Execute(null); // starts at T0, alarms at T0+30:00

        // Second trupp starts 5s later than the first, same 30-minute duration, so it crosses its
        // own threshold at T0+30:05 -- 5s after the first's ack, well inside the 15s repeat window.
        clock.Now = T0.AddSeconds(5);
        vm.NewMaxDurationMinutes = 30;
        vm.NewControlIntervalMinutes = 999;
        var second = Register(vm, truppfuehrer: "Huber", truppmann: "Mayer");
        second.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(30);
        ticker.Fire(); // first trupp alarms
        Assert.Single(alarm.Played);
        vm.AcknowledgeAlarmCommand.Execute(null);

        clock.Now = T0.AddMinutes(30).AddSeconds(5); // second trupp's own crossing, 5s after ack
        ticker.Fire();

        Assert.Equal(2, alarm.Played.Count); // announced immediately, not after waiting out 15s
    }

    // --- Druckabfrage audio cue (issue #78 follow-up) ---
    [Fact]
    public void Control_due_plays_the_cue_once_and_not_again_while_still_due()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(6); // past the 5-minute control interval
        ticker.Fire();
        ticker.Fire(); // must not sound a second time for the same due-crossing

        Assert.Single(alarm.Played);
        Assert.Equal(AlarmSound.PressureCheckDue, alarm.Played[0]);

        // The cue has to say which Trupp, or it is useless with more than one deployed.
        Assert.Contains("Trupp 1", alarm.Details[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_pressure_silences_the_cue_until_the_next_interval()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewControlIntervalMinutes = 5;
        var row = Register(vm);
        row.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(6);
        ticker.Fire();
        Assert.Single(alarm.Played);

        // A recorded reading re-anchors the next control interval, silencing the due state.
        row.PressureInput = 250;
        row.RecordPressureCommand.Execute(null);
        ticker.Fire();
        Assert.Single(alarm.Played); // still just the one from before

        clock.Now = T0.AddMinutes(12); // past the next 5-minute interval from the reading
        ticker.Fire();

        Assert.Equal(2, alarm.Played.Count);
        Assert.All(alarm.Played, s => Assert.Equal(AlarmSound.PressureCheckDue, s));
    }

    [Fact]
    public void Readonly_session_never_plays_the_control_due_cue()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = TestSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var seedVm = Vm(clock, seed);
        seedVm.NewControlIntervalMinutes = 5;
        var seedRow = Register(seedVm);
        seedRow.StartCommand.Execute(null);
        clock.Now = T0.AddMinutes(6);
        seed.Close();

        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        _ = Vm(clock, ro, ticker: ticker, alarm: alarm);
        ticker.Fire();

        Assert.Empty(alarm.Played);
    }

    [Fact]
    public void Two_trupps_due_at_different_times_each_sound_their_own_cue()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);

        vm.NewControlIntervalMinutes = 5;
        var first = Register(vm);
        first.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(2);
        vm.NewControlIntervalMinutes = 5;
        var second = Register(vm, truppfuehrer: "Huber", truppmann: "Mayer");
        second.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(6); // first trupp's interval elapsed, second's has not
        ticker.Fire();
        Assert.Single(alarm.Played);

        clock.Now = T0.AddMinutes(8); // second trupp's interval (registered/started 2 min later) now elapsed
        ticker.Fire();
        Assert.Equal(2, alarm.Played.Count);
    }

    // --- Crew entry (issue #15) ---
    [Fact]
    public void A_trupp_needs_both_crew_names_before_it_can_be_registered()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        vm.NewDesignation = "Angriffstrupp";

        vm.NewTruppfuehrer = "Müller";
        vm.AddTruppCommand.Execute(null);
        Assert.Equal(ValidationMessages.Required, vm.NewTruppmannError); // a Trupp is never one person
        Assert.Empty(session.Incident.ScbaTrupps);

        vm.NewTruppmann = "Schmidt";
        Assert.Null(vm.NewTruppmannError);
        vm.AddTruppCommand.Execute(null);
        Assert.Single(session.Incident.ScbaTrupps);
    }

    [Fact]
    public void A_three_person_trupp_type_reveals_and_requires_the_third_name()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        vm.NewDesignation = "Angriffstrupp";
        Assert.False(vm.RequiresThirdMember);

        vm.NewDesignation = CsaTrupp;
        Assert.True(vm.RequiresThirdMember);

        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.AddTruppCommand.Execute(null);
        Assert.Equal(ValidationMessages.Required, vm.NewZweiterTruppmannError);
        Assert.Empty(session.Incident.ScbaTrupps);

        vm.NewZweiterTruppmann = "Huber";
        Assert.Null(vm.NewZweiterTruppmannError);
        vm.AddTruppCommand.Execute(null);
        Assert.Single(session.Incident.ScbaTrupps);
    }

    [Fact]
    public void The_two_einsatzzeit_spinners_offer_the_same_range()
    {
        // They did not once: the Stammdaten editor went to 180 while this form stopped at 120, so a
        // Trupp-Typ configured above 120 could not be registered at its own Einsatzzeit. Both bind
        // this bound now, and the stored value is floored but never capped, so the bound only has
        // to be generous and, above all, shared.
        Assert.Equal(TruppTypeRow.MaxDurationLimitMinutes, ScbaViewModel.MaxDurationLimitMinutes);
        Assert.True(ScbaViewModel.MaxDurationLimitMinutes >= 240, "must cover a four-hour LPA");
    }

    [Fact]
    public void A_three_person_type_that_is_not_the_CSA_one_also_reveals_the_third_name()
    {
        // #418, and the point of the whole change: nothing about the third crew position is tied
        // to the word "CSA" any more. A Strahlenschutztrupp is three people because its
        // Stammdaten row says 3, and the form follows -- including the Einsatzzeit, which differs
        // from the CSA-Trupp's, so a rule keyed off the designation could not have covered both.
        var vm = Vm(new FixedClock(T0), NewSession(new FixedClock(T0)));

        vm.NewDesignation = "Strahlenschutztrupp";

        Assert.True(vm.RequiresThirdMember);
        Assert.Equal(30, vm.NewMaxDurationMinutes);

        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.AddTruppCommand.Execute(null);
        Assert.Equal(ValidationMessages.Required, vm.NewZweiterTruppmannError);

        vm.NewZweiterTruppmann = "Huber";
        Assert.Null(vm.NewZweiterTruppmannError);
    }

    [Fact]
    public void A_third_name_left_over_from_a_three_person_type_is_not_carried_into_an_ordinary_trupp()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = CsaTrupp;
        vm.NewZweiterTruppmann = "Huber";

        // Switching back to a two-person type must not smuggle the third name into the crew and
        // trip the domain's cardinality guard.
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        vm.AddTruppCommand.Execute(null);

        Assert.Equal("Müller / Schmidt", vm.Trupps[^1].Members);
    }

    [Fact]
    public void Registering_a_trupp_logs_the_full_crew_and_entry_pressure_to_the_etb()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);

        Register(vm, CsaTrupp, "Müller", "Schmidt", "Huber");

        Assert.Contains(
            session.Incident.Journal,
            e => e.Text == "Trupp 1 (CSA-Trupp) bereitgestellt: Müller / Schmidt / Huber, Einstiegsdruck 300 bar");
    }

    [Fact]
    public void The_row_exposes_the_crew_with_their_positions()
    {
        var clock = new FixedClock(T0);
        var row = Register(Vm(clock, NewSession(clock)));

        Assert.Equal("Müller / Schmidt", row.Members);
        Assert.Equal("Truppführer: Müller\nTruppmann: Schmidt", row.MembersDetail);
    }

    [Fact]
    public void Crew_inputs_reset_after_registering()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        Register(vm, CsaTrupp, "Müller", "Schmidt", "Huber");

        Assert.Equal(string.Empty, vm.NewTruppfuehrer);
        Assert.Equal(string.Empty, vm.NewTruppmann);
        Assert.Equal(string.Empty, vm.NewZweiterTruppmann);
    }

    // --- Truppnummer and Einstiegsdruck on the registration form (issue #78) ---
    [Fact]
    public void NewTruppNumber_defaults_to_the_next_free_number_and_advances_after_registering()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));

        Assert.Equal(1, vm.NewTruppNumber);
        var first = Register(vm);
        Assert.Equal(1, first.TruppNumber);

        Assert.Equal(2, vm.NewTruppNumber);
        var second = Register(vm, truppfuehrer: "Huber", truppmann: "Mayer");
        Assert.Equal(2, second.TruppNumber);
    }

    [Fact]
    public void NewTruppNumber_always_tracks_the_next_free_number_and_never_collides()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);

        // Truppnummer is internal, not user-editable (#217): a hand-typed duplicate used to crash
        // Incident.AddScbaTrupp. Another device (or a different code path) registering a Trupp
        // must re-suggest the next free number rather than leave a stale, now-taken one behind.
        session.AddScbaTrupp("Wassertrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300);

        Assert.Equal(2, vm.NewTruppNumber);
        var row = Register(vm, truppfuehrer: "Huber", truppmann: "Mayer");
        Assert.Equal(2, row.TruppNumber);
    }

    [Fact]
    public void The_grid_row_and_alarm_banner_use_the_TruppNumber_display_format()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        vm.NewTruppNumber = 5;

        var row = Register(vm);

        Assert.Equal(5, row.TruppNumber);
        Assert.Equal("Trupp 5 (Angriffstrupp)", row.DisplayName);
    }

    // --- Abfrage-Intervall defaults to a third of Einsatzzeit (issue #78) ---
    [Fact]
    public void NewControlIntervalMinutes_defaults_to_a_third_of_the_einsatzzeit()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings); // no type picked yet => 30 min

        Assert.Equal(10, vm.NewControlIntervalMinutes); // 30 / 3, truncated
    }

    [Fact]
    public void Switching_trupp_type_re_derives_the_control_interval_with_the_einsatzzeit()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = CsaTrupp; // its Stammdaten row says 22 minutes
        Assert.Equal(22, vm.NewMaxDurationMinutes);
        Assert.Equal(7, vm.NewControlIntervalMinutes); // 22 / 3, truncated
    }

    [Fact]
    public void A_hand_edited_control_interval_survives_an_einsatzzeit_change()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewControlIntervalMinutes = 4; // operator overrides
        vm.NewMaxDurationMinutes = 45;    // and separately changes the Einsatzzeit

        Assert.Equal(4, vm.NewControlIntervalMinutes); // not overwritten
    }

    [Fact]
    public void Registering_resets_the_control_interval_override()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);
        vm.NewControlIntervalMinutes = 4; // user override before adding

        Register(vm); // adds an Angriffstrupp, then resets the form

        Assert.Equal(10, vm.NewControlIntervalMinutes); // back to the no-type Einsatzzeit / 3
    }

    // Distinct Einsatzzeiten so a swapped mapping is caught; nothing here matches a compiled-in
    // default. Since #398 they live on the Trupp-Typ, not in three settings keyed by name.
    private static readonly TruppType[] CustomTruppTypes =
    {
        new("Angriffstrupp", 2, 35),
        new(CsaTrupp, 3, 22),
        new(LpaTrupp, 2, 48),
    };

    private static readonly IncidentSettings CustomSettings = new(
        IlsReminderIntervalMinutes: 15,
        IlsReminderFollowUpIntervalMinutes: 30,
        ReturnPressureBar: 55);

    private static ScbaViewModel VmWith(FixedClock clock, LocalIncidentSession session, IncidentSettings settings) =>
        new(
            session,
            MasterDataSet.Empty with { TruppTypes = CustomTruppTypes, Settings = settings },
            clock,
            new FakeTicker(),
            new FakeAlarmService(),
            () => { });

    [Fact]
    public void New_trupp_defaults_come_from_settings()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        // No designation picked yet, so the Einsatzzeit is the same fallback an unlisted type
        // gets. The Rueckzugsdruck is still a global setting.
        Assert.Equal(AtemschutzTrupp.DefaultMaxDurationMinutes, vm.NewMaxDurationMinutes);
        Assert.Equal(55, vm.NewReturnPressureBar);
    }

    [Fact]
    public void Selecting_a_trupp_type_suggests_the_einsatzzeit_from_its_stammdaten_row()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = CsaTrupp;
        Assert.Equal(22, vm.NewMaxDurationMinutes);

        vm.NewDesignation = "Angriffstrupp";
        Assert.Equal(35, vm.NewMaxDurationMinutes);
    }

    [Fact]
    public void A_designation_the_stammdaten_do_not_list_falls_back_rather_than_blocking()
    {
        // The whole point of #398: the rules follow the row, and a type with no row is simply an
        // ordinary Trupp. An Einsatz in progress is never blocked by a missing Stammdaten entry.
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = "Chemietrupp";

        Assert.False(vm.RequiresThirdMember);
        Assert.Equal(AtemschutzTrupp.DefaultMaxDurationMinutes, vm.NewMaxDurationMinutes);
    }

    [Fact]
    public void A_trupp_type_is_matched_the_way_every_other_stammdaten_value_is()
    {
        // Trimmed and ignoring case, like StammdatenCatalogue does elsewhere -- so a designation
        // that differs only in spacing still finds its row instead of silently losing its rules.
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = " csa-trupp ";

        Assert.True(vm.RequiresThirdMember);
        Assert.Equal(22, vm.NewMaxDurationMinutes);
    }

    [Fact]
    public void A_longer_einsatzzeit_type_suggests_its_own_value_and_keeps_two_people()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = LpaTrupp;
        Assert.Equal(48, vm.NewMaxDurationMinutes);   // its row: longer than an ordinary Trupp
        Assert.False(vm.RequiresThirdMember);         // ... but crewed by two

        vm.NewDesignation = "Angriffstrupp";
        Assert.Equal(35, vm.NewMaxDurationMinutes);
    }

    [Fact]
    public void A_hand_edited_einsatzzeit_survives_a_trupp_type_switch()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewMaxDurationMinutes = 45;                // user overrides
        vm.NewDesignation = CsaTrupp;

        Assert.Equal(45, vm.NewMaxDurationMinutes);   // not overwritten by the type's own value
    }

    [Fact]
    public void Registering_resets_the_einsatzzeit_and_clears_the_override()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);
        vm.NewMaxDurationMinutes = 45;                // user override before adding

        Register(vm);                                 // adds an Angriffstrupp, then resets the form

        // The form clears the designation too, so this is the no-type fallback again.
        Assert.Equal(AtemschutzTrupp.DefaultMaxDurationMinutes, vm.NewMaxDurationMinutes);

        // Override cleared: picking a type re-suggests its own Einsatzzeit again.
        vm.NewDesignation = CsaTrupp;
        Assert.Equal(22, vm.NewMaxDurationMinutes);
    }

    // ----- #422: the Atemschutz header bars lead to the Trupp they are talking about -----

    /// <summary>
    /// Registers and starts a Trupp with an explicit Einsatzzeit and Abfrage-Intervall. Order
    /// matters: the designation applies the type's defaults, so both overrides come after it.
    /// </summary>
    private static ScbaTruppRow StartTrupp(
        ScbaViewModel vm,
        string truppfuehrer,
        int maxDurationMinutes,
        int controlIntervalMinutes)
    {
        vm.NewDesignation = "Angriffstrupp";
        vm.NewMaxDurationMinutes = maxDurationMinutes;
        vm.NewControlIntervalMinutes = controlIntervalMinutes;
        vm.NewTruppfuehrer = truppfuehrer;
        vm.NewTruppmann = "Schmidt";
        vm.AddTruppCommand.Execute(null);
        var row = vm.Trupps[^1];
        row.StartCommand.Execute(null);
        return row;
    }

    [Fact]
    public void ShowMostUrgentControl_selects_the_trupp_whose_druckabfrage_is_soonest()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        var relaxed = StartTrupp(vm, "Müller", maxDurationMinutes: 60, controlIntervalMinutes: 30);
        var urgent = StartTrupp(vm, "Huber", maxDurationMinutes: 60, controlIntervalMinutes: 5);

        vm.ShowMostUrgentControlCommand.Execute(null);

        Assert.Same(urgent, vm.SelectedTrupp);
        Assert.NotSame(relaxed, vm.SelectedTrupp);
    }

    [Fact]
    public void ShowAlarmingTrupp_selects_the_alarming_trupp_not_the_one_due_for_a_druckabfrage()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));

        // Long Einsatzzeit, very short interval: overdue for a Druckabfrage, never in Alarm.
        var overdue = StartTrupp(vm, "Müller", maxDurationMinutes: 60, controlIntervalMinutes: 1);

        // Short Einsatzzeit, long interval: in Rückzugsalarm, not due for a Druckabfrage.
        var alarming = StartTrupp(vm, "Huber", maxDurationMinutes: 20, controlIntervalMinutes: 30);

        clock.Now = T0.AddMinutes(25);

        Assert.True(alarming.IsAlarm);
        Assert.False(overdue.IsAlarm);

        vm.ShowMostUrgentControlCommand.Execute(null);
        Assert.Same(overdue, vm.SelectedTrupp);

        vm.ShowAlarmingTruppCommand.Execute(null);
        Assert.Same(alarming, vm.SelectedTrupp);
    }

    [Fact]
    public void Showing_the_same_trupp_twice_asks_to_reveal_it_again()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        var row = StartTrupp(vm, "Müller", maxDurationMinutes: 60, controlIntervalMinutes: 5);

        var reveals = 0;
        vm.RevealRequested += (_, _) => reveals++;

        vm.ShowMostUrgentControlCommand.Execute(null);
        vm.ShowMostUrgentControlCommand.Execute(null);

        // The second tap changes no property, so a selection-change notification alone would not
        // fire. Scrolling back to a row the operator has since scrolled away from is the point.
        Assert.Same(row, vm.SelectedTrupp);
        Assert.Equal(2, reveals);
    }

    [Fact]
    public void Showing_a_trupp_when_there_is_none_selects_nothing_and_reveals_nothing()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        Register(vm);   // bereitgestellt, never started: not active, not alarming

        var reveals = 0;
        vm.RevealRequested += (_, _) => reveals++;

        vm.ShowMostUrgentControlCommand.Execute(null);
        vm.ShowAlarmingTruppCommand.Execute(null);

        Assert.Null(vm.SelectedTrupp);
        Assert.Equal(0, reveals);
    }

    [Fact]
    public void The_selected_trupp_survives_an_incident_wide_change()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);
        var row = StartTrupp(vm, "Müller", maxDurationMinutes: 60, controlIntervalMinutes: 5);
        vm.ShowMostUrgentControlCommand.Execute(null);

        // #294: rows are reconciled in place rather than cleared, so a change made anywhere in the
        // incident must not throw away what the operator has just been sent to look at.
        session.AddJournalEntry(EtbDirection.Outgoing, "Lagemeldung", from: null, to: null);

        Assert.Same(row, Assert.Single(vm.Trupps));
        Assert.Same(row, vm.SelectedTrupp);
    }
}
