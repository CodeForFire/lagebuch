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
    public void Add_trupp_appends_with_entry_time_and_pressure()
    {
        var incident = NewIncident(out var clock);
        clock.Now = T0.AddMinutes(3);

        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300, callSign: "FFB 1/40/1");

        Assert.Same(trupp, Assert.Single(incident.ScbaTrupps));
        Assert.Equal(T0.AddMinutes(3), trupp.EntryTime);
        Assert.Equal(300, trupp.EntryPressure);
        Assert.Equal(300, trupp.LatestPressure);
        Assert.True(trupp.IsActive);
    }

    [Fact]
    public void Pressure_readings_append_in_order_and_track_latest()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300);

        clock.Now = T0.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, 240);
        clock.Now = T0.AddMinutes(10);
        incident.RecordScbaPressure(clock, trupp.Id, 180);

        Assert.Equal(2, trupp.PressureReadings.Count);
        Assert.Equal(240, trupp.PressureReadings[0].Bar);
        Assert.Equal(180, trupp.LatestPressure);
    }

    [Fact]
    public void Mark_returned_sets_exit_and_flips_active()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300);

        clock.Now = T0.AddMinutes(18);
        incident.MarkScbaReturned(clock, trupp.Id);

        Assert.False(trupp.IsActive);
        Assert.Equal(T0.AddMinutes(18), trupp.ExitTime);
        Assert.False(trupp.IsAlarm(T0.AddHours(2))); // returned trupp never alarms
    }

    [Fact]
    public void Time_alarm_fires_at_max_duration()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300, maxDurationMinutes: 30);

        Assert.False(trupp.IsTimeAlarm(T0.AddMinutes(29).AddSeconds(59)));
        Assert.True(trupp.IsTimeAlarm(T0.AddMinutes(30)));
    }

    [Fact]
    public void Pressure_alarm_fires_at_return_threshold()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300, returnPressureBar: 60);

        Assert.False(trupp.IsPressureAlarm);
        incident.RecordScbaPressure(clock, trupp.Id, 60);
        Assert.True(trupp.IsPressureAlarm);
    }

    [Fact]
    public void Closed_incident_rejects_scba_mutations()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300);
        var op = new SessionOperator("Müller");
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AddScbaTrupp(clock, "Wassertrupp", "A / B", 300));
        Assert.Throws<IncidentClosedException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<IncidentClosedException>(() => incident.MarkScbaReturned(clock, trupp.Id));
    }

    [Fact]
    public void Validation_rejects_bad_input()
    {
        var incident = NewIncident(out var clock);

        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(clock, "Angriffstrupp", "  ", 300));
        Assert.Throws<ArgumentException>(() => incident.AddScbaTrupp(clock, "  ", "Müller", 300));
        Assert.Throws<ArgumentOutOfRangeException>(() => incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller", 500));
    }

    [Fact]
    public void Recording_pressure_or_returning_after_return_throws()
    {
        var incident = NewIncident(out var clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", "Müller / Schmidt", 300);
        incident.MarkScbaReturned(clock, trupp.Id);

        Assert.Throws<InvalidOperationException>(() => incident.RecordScbaPressure(clock, trupp.Id, 200));
        Assert.Throws<InvalidOperationException>(() => incident.MarkScbaReturned(clock, trupp.Id));
    }

    [Fact]
    public void Unknown_trupp_id_throws()
    {
        var incident = NewIncident(out var clock);
        Assert.Throws<KeyNotFoundException>(() => incident.RecordScbaPressure(clock, Guid.NewGuid(), 200));
    }
}
