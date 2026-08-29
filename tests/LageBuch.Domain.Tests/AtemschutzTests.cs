using LageBuch.Domain.Atemschutz;

namespace LageBuch.Domain.Tests;

public class AtemschutzTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock)
    {
        clock = new FixedClock(T0);
        return Incident.Start(clock, new SessionOperator("Müller", "FFB 12/1"));
    }

    [Fact]
    public void Registered_trupp_is_waiting_and_clock_not_running()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, callSign: "FFB 1/40/1");

        Assert.Same(trupp, Assert.Single(incident.ScbaTrupps));
        Assert.True(trupp.IsWaiting);
        Assert.False(trupp.IsActive);
        Assert.Null(trupp.StartTime);
        // Entry pressure is captured at registration now, not at Start.
        Assert.Equal(300, trupp.EntryPressure);
        // A waiting trupp's safety clock does not run regardless of how much time passes.
        Assert.False(trupp.IsTimeAlarm(T0.AddHours(2)));
    }

    [Fact]
    public void Register_rejects_non_positive_entry_pressure()
    {
        var incident = NewIncident(out var clock);
        Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddScbaTrupp(
            clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 0));
    }

    [Fact]
    public void Start_sends_trupp_under_air_without_changing_entry_pressure()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);

        clock.Now = T0.AddMinutes(7);
        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.True(trupp.IsActive);
        Assert.Equal(T0.AddMinutes(7), trupp.StartTime);
        Assert.Equal(300, trupp.EntryPressure);
        Assert.Equal(300, trupp.LatestPressure);
    }

    [Fact]
    public void Time_alarm_is_anchored_on_start_not_registration()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, maxDurationMinutes: 30);

        // Registered at T0 but waits 10 minutes before going under air.
        clock.Now = T0.AddMinutes(10);
        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.False(trupp.IsTimeAlarm(T0.AddMinutes(39)));  // 29 min under air
        Assert.True(trupp.IsTimeAlarm(T0.AddMinutes(40)));   // 30 min under air
    }

    [Fact]
    public void Pressure_readings_append_and_track_latest()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);

        clock.Now = T0.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 240);
        clock.Now = T0.AddMinutes(10);
        incident.RecordScbaPressure(clock, trupp.Id, 180);

        Assert.Equal(2, trupp.PressureReadings.Count);
        Assert.Equal(240, trupp.PressureReadings[0].Bar);
        Assert.Equal(180, trupp.LatestPressure);
    }

    [Fact]
    public void Pressure_control_countdown_resets_on_each_reading()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, pressureControlIntervalMinutes: 5);
        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.False(trupp.IsControlDue(T0.AddMinutes(4)));
        Assert.True(trupp.IsControlDue(T0.AddMinutes(5)));

        // Recording a pressure re-anchors the next control to that moment.
        clock.Now = T0.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 250);
        Assert.False(trupp.IsControlDue(T0.AddMinutes(9)));
        Assert.True(trupp.IsControlDue(T0.AddMinutes(10)));
    }

    [Fact]
    public void Withdraw_requires_an_active_trupp()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);

        // Still waiting -- has not gone under air yet.
        Assert.Throws<InvalidOperationException>(() => incident.WithdrawScbaTrupp(clock, trupp.Id));

        incident.StartScbaTrupp(clock, trupp.Id);
        incident.WithdrawScbaTrupp(clock, trupp.Id);

        // Already withdrawing -- cannot withdraw twice.
        Assert.Throws<InvalidOperationException>(() => incident.WithdrawScbaTrupp(clock, trupp.Id));
    }

    [Fact]
    public void MarkRemoved_requires_a_prior_withdraw()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);

        // Under air but not yet withdrawing -- abgenommen is not reachable directly.
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaRemoved(clock, trupp.Id));
    }

    [Fact]
    public void Withdraw_then_MarkRemoved_completes_the_lifecycle()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);
        Assert.True(trupp.IsActive);

        clock.Now = T0.AddMinutes(15);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        Assert.True(trupp.IsWithdrawing);
        Assert.False(trupp.IsActive);
        Assert.Equal(T0.AddMinutes(15), trupp.WithdrawTime);

        clock.Now = T0.AddMinutes(18);
        incident.MarkScbaRemoved(clock, trupp.Id);
        Assert.True(trupp.IsReturned);
        Assert.False(trupp.IsWithdrawing);
        Assert.Equal(T0.AddMinutes(18), trupp.ExitTime);
        Assert.False(trupp.IsAlarm(T0.AddHours(2)));
    }

    [Fact]
    public void Alarm_and_control_due_stay_true_while_withdrawing()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, maxDurationMinutes: 30, pressureControlIntervalMinutes: 5);
        incident.StartScbaTrupp(clock, trupp.Id);
        incident.WithdrawScbaTrupp(clock, trupp.Id);

        // A withdrawing crew is still consuming air -- monitoring does not stop at Rückzug.
        Assert.True(trupp.IsTimeAlarm(T0.AddMinutes(30)));
        Assert.True(trupp.IsControlDue(T0.AddMinutes(5)));

        incident.RecordScbaPressure(clock, trupp.Id, 40); // below the default 50 bar return threshold
        Assert.True(trupp.IsPressureAlarm);
    }

    [Fact]
    public void Pressure_alarm_fires_at_return_threshold()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, returnPressureBar: 60);
        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.False(trupp.IsPressureAlarm);
        incident.RecordScbaPressure(clock, trupp.Id, 60);
        Assert.True(trupp.IsPressureAlarm);
    }

    [Fact]
    public void Closed_incident_rejects_scba_mutations()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);
        incident.Close(clock, new SessionOperator("Müller"));

        Assert.Throws<IncidentClosedException>(() => incident.AddScbaTrupp(
            clock, "Wassertrupp", TruppMember.Crew("A", "B"), entryPressure: 300));
        Assert.Throws<IncidentClosedException>(() => incident.StartScbaTrupp(clock, trupp.Id));
        Assert.Throws<IncidentClosedException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<IncidentClosedException>(() => incident.WithdrawScbaTrupp(clock, trupp.Id));
        Assert.Throws<IncidentClosedException>(() => incident.MarkScbaRemoved(clock, trupp.Id));
    }

    [Fact]
    public void Validation_rejects_bad_input()
    {
        var incident = NewIncident(out var clock);

        Assert.Throws<ArgumentException>(
            () => incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "  "), entryPressure: 300));
        Assert.Throws<ArgumentException>(
            () => incident.AddScbaTrupp(clock, "  ", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 500));
    }

    // --- Crew cardinality (issue #15) ---

    [Fact]
    public void An_ordinary_trupp_is_exactly_two_people()
    {
        var incident = NewIncident(out var clock);

        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);

        Assert.Equal(2, trupp.Members.Count);
        Assert.Equal(TruppRole.Truppfuehrer, trupp.Members[0].Role);
        Assert.Equal("Müller", trupp.Members[0].Name);
        Assert.Equal(TruppRole.Truppmann, trupp.Members[1].Role);
        Assert.Equal("Müller / Schmidt", trupp.MembersDisplay);
    }

    [Fact]
    public void A_single_person_is_not_a_trupp()
    {
        var incident = NewIncident(out var clock);
        var lone = new[] { TruppMember.Create(TruppRole.Truppfuehrer, "Müller") };

        // The whole point of issue #15: Atemschutz is never a solo activity.
        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(
            clock, "Angriffstrupp", lone, entryPressure: 300));
    }

    [Fact]
    public void An_ordinary_trupp_rejects_a_third_person()
    {
        var incident = NewIncident(out var clock);

        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(
            clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt", "Huber"), entryPressure: 300));
    }

    [Fact]
    public void A_csa_trupp_is_exactly_three_people()
    {
        var incident = NewIncident(out var clock);

        var trupp = incident.AddScbaTrupp(
            clock, AtemschutzTrupp.ChemicalTruppDesignation, TruppMember.Crew("Müller", "Schmidt", "Huber"),
            entryPressure: 300);

        Assert.Equal(3, trupp.Members.Count);
        Assert.Equal(TruppRole.ZweiterTruppmann, trupp.Members[2].Role);
        Assert.Equal("Müller / Schmidt / Huber", trupp.MembersDisplay);
    }

    [Fact]
    public void A_csa_trupp_rejects_a_crew_of_two()
    {
        var incident = NewIncident(out var clock);

        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(
            clock, AtemschutzTrupp.ChemicalTruppDesignation, TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300));
    }

    [Fact]
    public void The_same_position_cannot_be_filled_twice()
    {
        var incident = NewIncident(out var clock);
        var duplicated = new[]
        {
            TruppMember.Create(TruppRole.Truppfuehrer, "Müller"),
            TruppMember.Create(TruppRole.Truppfuehrer, "Schmidt"),
        };

        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(
            clock, "Angriffstrupp", duplicated, entryPressure: 300));
    }

    [Fact]
    public void Rehydrate_accepts_a_crew_the_current_rules_would_reject()
    {
        // Stored Trupps are history. Refusing to load an incident because an old record has an
        // odd crew size would make the file unreadable rather than merely imperfect.
        var trupp = AtemschutzTrupp.Rehydrate(
            Guid.NewGuid(), 1, T0, null, null, "Angriffstrupp",
            new[] { TruppMember.Create(TruppRole.Truppfuehrer, "Allein") },
            null, null, null, 30, 60, 5, null, Array.Empty<PressureReading>());

        Assert.Single(trupp.Members);
        Assert.Equal("Allein", trupp.MembersDisplay);
    }

    [Fact]
    public void Rehydrate_accepts_a_legacy_row_with_no_entry_pressure()
    {
        // Pre-migration rows may not have a recorded entry pressure at all.
        var trupp = AtemschutzTrupp.Rehydrate(
            Guid.NewGuid(), 4, T0, T0.AddMinutes(2), null, "Angriffstrupp",
            TruppMember.Crew("Müller", "Schmidt"),
            null, null, null, 30, 50, 5, null, Array.Empty<PressureReading>());

        Assert.Null(trupp.EntryPressure);
        Assert.Equal(4, trupp.TruppNumber);
    }

    [Fact]
    public void Cannot_act_out_of_order()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);

        // Before start: recording pressure, withdrawing, or marking removed is invalid.
        Assert.Throws<InvalidOperationException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<InvalidOperationException>(() => incident.WithdrawScbaTrupp(clock, trupp.Id));
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaRemoved(clock, trupp.Id));

        incident.StartScbaTrupp(clock, trupp.Id);
        Assert.Throws<InvalidOperationException>(() => incident.StartScbaTrupp(clock, trupp.Id));

        // Under air but not yet withdrawn: abgenommen is not reachable directly.
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaRemoved(clock, trupp.Id));

        incident.WithdrawScbaTrupp(clock, trupp.Id);
        incident.MarkScbaRemoved(clock, trupp.Id);
        Assert.Throws<InvalidOperationException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaRemoved(clock, trupp.Id));
    }

    [Fact]
    public void Unknown_trupp_id_throws()
    {
        var incident = NewIncident(out var clock);
        Assert.Throws<KeyNotFoundException>(() => incident.StartScbaTrupp(clock, Guid.NewGuid()));
    }

    [Fact]
    public void Elapsed_stops_when_the_trupp_returns()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);
        clock.Now = T0.AddMinutes(12);
        incident.WithdrawScbaTrupp(clock, trupp.Id);
        incident.MarkScbaRemoved(clock, trupp.Id);

        // Time under air is a fact about the past. Left running, a Trupp returned months ago
        // reads as tens of thousands of hours -- which is what the live grid was showing.
        Assert.Equal(TimeSpan.FromMinutes(12), trupp.Elapsed(T0.AddMinutes(12)));
        Assert.Equal(TimeSpan.FromMinutes(12), trupp.Elapsed(T0.AddDays(30)));
    }

    [Fact]
    public void Elapsed_still_runs_while_the_trupp_is_under_air()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.Equal(TimeSpan.FromMinutes(7), trupp.Elapsed(T0.AddMinutes(7)));
    }

    [Theory]
    [InlineData("LPA-Trupp", true)]
    [InlineData("lpa-trupp", true)]
    [InlineData(" LPA-Trupp ", true)]
    [InlineData("Angriffstrupp", false)]
    [InlineData("CSA-Trupp", false)]
    public void IsLpaTrupp_recognises_the_LPA_designation(string designation, bool expected) =>
        Assert.Equal(expected, AtemschutzTrupp.IsLpaTrupp(designation));

    [Fact]
    public void An_LPA_trupp_keeps_the_standard_two_person_crew() =>
        Assert.Equal(AtemschutzTrupp.StandardMemberCount,
            AtemschutzTrupp.RequiredMemberCount(AtemschutzTrupp.LpaTruppDesignation));

    [Fact]
    public void Return_pressure_defaults_to_50_bar()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);

        Assert.Equal(50, trupp.ReturnPressureBar);
    }

    // --- Truppnummer (issue #78) ---

    [Fact]
    public void First_trupp_is_assigned_number_one()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);

        Assert.Equal(1, trupp.TruppNumber);
    }

    [Fact]
    public void Successive_trupps_get_successive_numbers()
    {
        var incident = NewIncident(out var clock);
        var first = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300);
        var second = incident.AddScbaTrupp(clock, "Wassertrupp", TruppMember.Crew("Huber", "Bauer"),
            entryPressure: 300);

        Assert.Equal(1, first.TruppNumber);
        Assert.Equal(2, second.TruppNumber);
    }

    [Fact]
    public void NextFreeScbaTruppNumber_fills_a_gap_left_by_an_explicit_assignment()
    {
        var incident = NewIncident(out var clock);
        incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300); // 1
        incident.AddScbaTrupp(clock, "Wassertrupp", TruppMember.Crew("Huber", "Bauer"),
            entryPressure: 300, truppNumber: 5);

        Assert.Equal(2, incident.NextFreeScbaTruppNumber());
    }

    [Fact]
    public void An_explicit_truppNumber_can_be_supplied_instead_of_auto_assignment()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, truppNumber: 7);

        Assert.Equal(7, trupp.TruppNumber);
    }

    [Fact]
    public void Registering_a_duplicate_truppNumber_throws()
    {
        var incident = NewIncident(out var clock);
        incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, truppNumber: 3);

        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(
            clock, "Wassertrupp", TruppMember.Crew("Huber", "Bauer"), entryPressure: 300, truppNumber: 3));
    }

    [Fact]
    public void DisplayName_combines_truppNumber_and_designation()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            entryPressure: 300, truppNumber: 3);

        Assert.Equal("Trupp 3 (Angriffstrupp)", trupp.DisplayName);
    }

    [Fact]
    public void FormatDisplayName_matches_the_instance_property()
    {
        Assert.Equal("Trupp 2 (Wassertrupp)", AtemschutzTrupp.FormatDisplayName(2, "Wassertrupp"));
    }
}
