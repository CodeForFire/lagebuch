using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class ScbaViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static MasterDataSet Md() => MasterDataSet.Empty with
    {
        RadioCallSigns = new[] { "FFB 1/40/1" },
        TruppTypes = new[] { "Angriffstrupp", "Wassertrupp" },
    };

    private static LocalIncidentSession NewSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(new FakeStore(), clock,
            new SessionOperator("Müller", "FFB 12/1"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());

    private static ScbaViewModel Vm(FixedClock clock, LocalIncidentSession session, Action? onChanged = null, FakeTicker? ticker = null, FakeAlarmService? alarm = null) =>
        new(session, Md(), clock, ticker ?? new FakeTicker(), alarm ?? new FakeAlarmService(), onChanged ?? (() => { }));

    private static ScbaTruppRow Register(ScbaViewModel vm, string designation = "Angriffstrupp",
        string truppfuehrer = "Müller", string truppmann = "Schmidt", string? zweiterTruppmann = null)
    {
        vm.NewDesignation = designation;
        vm.NewTruppfuehrer = truppfuehrer;
        vm.NewTruppmann = truppmann;
        vm.NewZweiterTruppmann = zweiterTruppmann ?? string.Empty;
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
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("bereitgestellt"));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void AddTrupp_disabled_when_required_fields_blank()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "  ";
        vm.NewTruppmann = "Schmidt";
        Assert.False(vm.AddTruppCommand.CanExecute(null));
    }

    [Fact]
    public void AddTrupp_disabled_when_entry_pressure_is_not_positive()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = "Angriffstrupp";
        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        Assert.True(vm.AddTruppCommand.CanExecute(null)); // default entry pressure is positive

        vm.NewEntryPressure = 0;
        Assert.False(vm.AddTruppCommand.CanExecute(null));
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
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("im Einsatz"));
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
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Druckkontrolle") && e.Text.Contains("45 bar"));
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Rückzugsalarm"));
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
        Assert.Contains("Nächste Druckabfrage", vm.NextControlDisplay);
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
        Assert.Contains("fällig", vm.NextControlDisplay);
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

        var alarms = session.Incident.Journal.Where(e => e.Text.Contains("Rückzugsalarm")).ToList();
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
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("Rückzug"));

        clock.Now = T0.AddMinutes(12);
        row.MarkRemovedCommand.Execute(null);

        Assert.True(row.IsReturned);
        Assert.Equal("—", row.RemainingDisplay);
        Assert.False(vm.HasControlReminder);
        Assert.Contains(session.Incident.Journal, e => e.Text.Contains("abgenommen"));
    }

    [Fact]
    public void Readonly_session_disables_actions()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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
        var row = Register(vm);
        row.StartCommand.Execute(null);

        Assert.False(vm.IsAnyAlarm);
        Assert.False(alarm.IsSounding);

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();

        Assert.True(vm.IsAnyAlarm);
        Assert.True(alarm.IsSounding);
        Assert.Contains("RÜCKZUGSALARM", vm.AlarmDisplay);
        Assert.Contains("Trupp 1 (Angriffstrupp)", vm.AlarmDisplay);
        Assert.True(vm.AcknowledgeAlarmCommand.CanExecute(null));
    }

    [Fact]
    public void Acknowledging_alarm_silences_sound_but_keeps_banner()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewMaxDurationMinutes = 30;
        var row = Register(vm);
        row.StartCommand.Execute(null);
        clock.Now = T0.AddMinutes(31);
        ticker.Fire();

        vm.AcknowledgeAlarmCommand.Execute(null);

        Assert.False(alarm.IsSounding); // sound stopped
        Assert.True(vm.IsAnyAlarm);     // but banner remains while the alarm condition persists

        // A further tick with the alarm acknowledged must not re-arm the sound.
        ticker.Fire();
        Assert.False(alarm.IsSounding);
    }

    [Fact]
    public void Alarm_and_control_reminder_persist_through_Rueckzug_and_clear_on_Abgenommen()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);
        vm.NewMaxDurationMinutes = 30;
        var row = Register(vm);
        row.StartCommand.Execute(null);
        clock.Now = T0.AddMinutes(31);
        ticker.Fire();
        Assert.True(alarm.IsSounding);

        // Rückzug alone does not silence the alarm -- the crew is still consuming air.
        row.WithdrawCommand.Execute(null);
        Assert.True(vm.IsAnyAlarm);

        row.MarkRemovedCommand.Execute(null);

        Assert.False(vm.IsAnyAlarm);
        Assert.False(alarm.IsSounding);
    }

    [Fact]
    public void A_second_trupp_newly_alarming_re_arms_the_sound_after_ack()
    {
        var clock = new FixedClock(T0);
        var ticker = new FakeTicker();
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), ticker: ticker, alarm: alarm);

        vm.NewMaxDurationMinutes = 30;
        var first = Register(vm);
        first.StartCommand.Execute(null);

        // Second trupp goes under air 10 minutes later, so its limit falls after the first's.
        clock.Now = T0.AddMinutes(10);
        vm.NewMaxDurationMinutes = 30;
        var second = Register(vm, truppfuehrer: "Huber", truppmann: "Mayer");
        second.StartCommand.Execute(null);

        clock.Now = T0.AddMinutes(31);
        ticker.Fire();              // first trupp alarms (limit at T0+30)
        vm.AcknowledgeAlarmCommand.Execute(null);
        Assert.False(alarm.IsSounding);

        clock.Now = T0.AddMinutes(41);
        ticker.Fire();              // second trupp now past its limit (T0+40) → re-arm

        Assert.True(alarm.IsSounding);
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
        Assert.Equal(AlarmSound.ScbaControlDue, alarm.Played[0]);
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
        Assert.All(alarm.Played, s => Assert.Equal(AlarmSound.ScbaControlDue, s));
    }

    [Fact]
    public void Readonly_session_never_plays_the_control_due_cue()
    {
        var clock = new FixedClock(T0);
        var store = new FakeStore();
        var seed = LocalIncidentSession.StartNew(store, clock, new SessionOperator("Müller"), "/x.fwincident", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
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

    [Fact]
    public void Dispose_stops_the_alarm()
    {
        var clock = new FixedClock(T0);
        var alarm = new FakeAlarmService();
        var vm = Vm(clock, NewSession(clock), alarm: alarm);

        vm.Dispose();

        Assert.True(alarm.StopCount >= 1);
    }

    // --- Crew entry (issue #15) ---

    [Fact]
    public void A_trupp_needs_both_crew_names_before_it_can_be_registered()
    {
        var vm = Vm(new FixedClock(T0), NewSession(new FixedClock(T0)));
        vm.NewDesignation = "Angriffstrupp";

        vm.NewTruppfuehrer = "Müller";
        Assert.False(vm.AddTruppCommand.CanExecute(null)); // a Trupp is never one person

        vm.NewTruppmann = "Schmidt";
        Assert.True(vm.AddTruppCommand.CanExecute(null));
    }

    [Fact]
    public void A_csa_trupp_reveals_and_requires_the_third_name()
    {
        var vm = Vm(new FixedClock(T0), NewSession(new FixedClock(T0)));
        vm.NewDesignation = "Angriffstrupp";
        Assert.False(vm.RequiresThirdMember);

        vm.NewDesignation = AtemschutzTrupp.ChemicalTruppDesignation;
        Assert.True(vm.RequiresThirdMember);

        vm.NewTruppfuehrer = "Müller";
        vm.NewTruppmann = "Schmidt";
        Assert.False(vm.AddTruppCommand.CanExecute(null));

        vm.NewZweiterTruppmann = "Huber";
        Assert.True(vm.AddTruppCommand.CanExecute(null));
    }

    [Fact]
    public void A_third_name_left_over_from_a_csa_selection_is_not_carried_into_an_ordinary_trupp()
    {
        var clock = new FixedClock(T0);
        var vm = Vm(clock, NewSession(clock));
        vm.NewDesignation = AtemschutzTrupp.ChemicalTruppDesignation;
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

        Register(vm, AtemschutzTrupp.ChemicalTruppDesignation, "Müller", "Schmidt", "Huber");

        Assert.Contains(session.Incident.Journal,
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
        Register(vm, AtemschutzTrupp.ChemicalTruppDesignation, "Müller", "Schmidt", "Huber");

        Assert.Equal("", vm.NewTruppfuehrer);
        Assert.Equal("", vm.NewTruppmann);
        Assert.Equal("", vm.NewZweiterTruppmann);
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
    public void A_hand_edited_truppNumber_is_not_clobbered_by_another_devices_registration()
    {
        var clock = new FixedClock(T0);
        var session = NewSession(clock);
        var vm = Vm(clock, session);

        vm.NewTruppNumber = 9; // operator overrides the suggested number

        // Another device (or this one, via a different code path) registers a Trupp, which fires
        // session.Changed and would normally re-suggest -- but the hand edit must survive.
        session.AddScbaTrupp("Wassertrupp", TruppMember.Crew("Bauer", "Klein"), entryPressure: 300);

        Assert.Equal(9, vm.NewTruppNumber);
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
        var vm = VmWith(clock, NewSession(clock), CustomSettings); // AGT default 35 minutes

        Assert.Equal(11, vm.NewControlIntervalMinutes); // 35 / 3, truncated
    }

    [Fact]
    public void Switching_trupp_type_re_derives_the_control_interval_with_the_einsatzzeit()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = AtemschutzTrupp.ChemicalTruppDesignation; // CSA default 22 minutes
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

        Assert.Equal(11, vm.NewControlIntervalMinutes); // back to AGT-Einsatzzeit / 3
    }

    // Distinct values so a swapped mapping is caught; nothing here matches a compiled-in default.
    private static readonly IncidentSettings CustomSettings = new(
        IlsReminderIntervalMinutes: 15, IlsReminderFollowUpIntervalMinutes: 30,
        AgtMaxDurationMinutes: 35, CsaMaxDurationMinutes: 22,
        LpaMaxDurationMinutes: 48, PressureControlIntervalMinutes: 7, ReturnPressureBar: 55);

    private static ScbaViewModel VmWith(FixedClock clock, LocalIncidentSession session, IncidentSettings settings) =>
        new(session, MasterDataSet.Empty with { TruppTypes = new[] { "Angriffstrupp" }, Settings = settings },
            clock, new FakeTicker(), new FakeAlarmService(), () => { });

    [Fact]
    public void New_trupp_defaults_come_from_settings()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        Assert.Equal(35, vm.NewMaxDurationMinutes);   // no designation yet => AGT default
        Assert.Equal(55, vm.NewReturnPressureBar);
    }

    [Fact]
    public void Selecting_a_CSA_trupp_suggests_the_CSA_einsatzzeit_and_reverts_for_an_AGT()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = AtemschutzTrupp.ChemicalTruppDesignation;
        Assert.Equal(22, vm.NewMaxDurationMinutes);   // CSA default

        vm.NewDesignation = "Angriffstrupp";
        Assert.Equal(35, vm.NewMaxDurationMinutes);   // back to AGT default
    }

    [Fact]
    public void Selecting_an_LPA_trupp_suggests_the_LPA_einsatzzeit_and_reverts_for_an_AGT()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewDesignation = AtemschutzTrupp.LpaTruppDesignation;
        Assert.Equal(48, vm.NewMaxDurationMinutes);   // LPA default (longer than AGT)

        vm.NewDesignation = "Angriffstrupp";
        Assert.Equal(35, vm.NewMaxDurationMinutes);   // back to AGT default
    }

    [Fact]
    public void A_hand_edited_einsatzzeit_survives_a_trupp_type_switch()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);

        vm.NewMaxDurationMinutes = 45;                // user overrides
        vm.NewDesignation = AtemschutzTrupp.ChemicalTruppDesignation;

        Assert.Equal(45, vm.NewMaxDurationMinutes);   // not overwritten by the CSA default
    }

    [Fact]
    public void Registering_resets_the_einsatzzeit_to_the_AGT_default_and_clears_the_override()
    {
        var clock = new FixedClock(T0);
        var vm = VmWith(clock, NewSession(clock), CustomSettings);
        vm.NewMaxDurationMinutes = 45;                // user override before adding

        Register(vm);                                 // adds an Angriffstrupp, then resets the form

        Assert.Equal(35, vm.NewMaxDurationMinutes);   // reset to AGT default
        // Override cleared: a CSA selection now re-suggests the CSA default again.
        vm.NewDesignation = AtemschutzTrupp.ChemicalTruppDesignation;
        Assert.Equal(22, vm.NewMaxDurationMinutes);
    }
}
