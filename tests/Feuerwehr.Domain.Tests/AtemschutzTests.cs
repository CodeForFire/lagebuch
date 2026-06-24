namespace Feuerwehr.Domain.Tests;

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
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", callSign: "FFB 1/40/1");

        Assert.Same(trupp, Assert.Single(incident.ScbaTrupps));
        Assert.True(trupp.IsWaiting);
        Assert.False(trupp.IsActive);
        Assert.Null(trupp.StartTime);
        Assert.Null(trupp.StartPressure);
        // A waiting trupp's safety clock does not run regardless of how much time passes.
        Assert.False(trupp.IsTimeAlarm(T0.AddHours(2)));
    }

    [Fact]
    public void Start_sends_trupp_under_air_and_records_start_pressure()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt");

        clock.Now = T0.AddMinutes(7);
        incident.StartScbaTrupp(clock, trupp.Id, 300);

        Assert.True(trupp.IsActive);
        Assert.Equal(T0.AddMinutes(7), trupp.StartTime);
        Assert.Equal(300, trupp.StartPressure);
        Assert.Equal(300, trupp.LatestPressure);
    }

    [Fact]
    public void Time_alarm_is_anchored_on_start_not_registration()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", maxDurationMinutes: 30);

        // Registered at T0 but waits 10 minutes before going under air.
        clock.Now = T0.AddMinutes(10);
        incident.StartScbaTrupp(clock, trupp.Id, 300);

        Assert.False(trupp.IsTimeAlarm(T0.AddMinutes(39)));  // 29 min under air
        Assert.True(trupp.IsTimeAlarm(T0.AddMinutes(40)));   // 30 min under air
    }

    [Fact]
    public void Pressure_readings_append_and_track_latest()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt");
        incident.StartScbaTrupp(clock, trupp.Id, 300);

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
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt",
            pressureControlIntervalMinutes: 5);
        incident.StartScbaTrupp(clock, trupp.Id, 300);

        Assert.False(trupp.IsControlDue(T0.AddMinutes(4)));
        Assert.True(trupp.IsControlDue(T0.AddMinutes(5)));

        // Recording a pressure re-anchors the next control to that moment.
        clock.Now = T0.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 250);
        Assert.False(trupp.IsControlDue(T0.AddMinutes(9)));
        Assert.True(trupp.IsControlDue(T0.AddMinutes(10)));
    }

    [Fact]
    public void Mark_returned_sets_exit_and_clears_active()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt");
        incident.StartScbaTrupp(clock, trupp.Id, 300);

        clock.Now = T0.AddMinutes(18);
        incident.MarkScbaReturned(clock, trupp.Id);

        Assert.True(trupp.IsReturned);
        Assert.False(trupp.IsActive);
        Assert.Equal(T0.AddMinutes(18), trupp.ExitTime);
        Assert.False(trupp.IsAlarm(T0.AddHours(2)));
    }

    [Fact]
    public void Pressure_alarm_fires_at_return_threshold()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", returnPressureBar: 60);
        incident.StartScbaTrupp(clock, trupp.Id, 300);

        Assert.False(trupp.IsPressureAlarm);
        incident.RecordScbaPressure(clock, trupp.Id, 60);
        Assert.True(trupp.IsPressureAlarm);
    }

    [Fact]
    public void Closed_incident_rejects_scba_mutations()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt");
        incident.StartScbaTrupp(clock, trupp.Id, 300);
        incident.Close(clock, new SessionOperator("Müller"));

        Assert.Throws<IncidentClosedException>(() => incident.AddScbaTrupp(clock, "Wassertrupp", "A / B"));
        Assert.Throws<IncidentClosedException>(() => incident.StartScbaTrupp(clock, trupp.Id, 280));
        Assert.Throws<IncidentClosedException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<IncidentClosedException>(() => incident.MarkScbaReturned(clock, trupp.Id));
    }

    [Fact]
    public void Validation_rejects_bad_input()
    {
        var incident = NewIncident(out var clock);

        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(clock, "Angriffstrupp", "  "));
        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(clock, "  ", "Müller"));

        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller");
        Assert.Throws<ArgumentOutOfRangeException>(() => incident.StartScbaTrupp(clock, trupp.Id, 500));
    }

    [Fact]
    public void Cannot_start_twice_or_act_before_start_or_after_return()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt");

        // Before start: recording pressure / returning is invalid.
        Assert.Throws<InvalidOperationException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaReturned(clock, trupp.Id));

        incident.StartScbaTrupp(clock, trupp.Id, 300);
        Assert.Throws<InvalidOperationException>(() => incident.StartScbaTrupp(clock, trupp.Id, 280));

        incident.MarkScbaReturned(clock, trupp.Id);
        Assert.Throws<InvalidOperationException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaReturned(clock, trupp.Id));
    }

    [Fact]
    public void Unknown_trupp_id_throws()
    {
        var incident = NewIncident(out var clock);
        Assert.Throws<KeyNotFoundException>(() => incident.StartScbaTrupp(clock, Guid.NewGuid(), 300));
    }
}
