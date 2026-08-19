using Feuerwehr.Domain.Time;

namespace Feuerwehr.Domain.Tests;

public class IncidentTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 23, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident() =>
        Incident.Start(new FixedClock(T0), new SessionOperator("Müller", "FFB 12/1"));

    [Fact]
    public void UpsertTimer_records_state_findable_by_key()
    {
        var incident = NewIncident();

        incident.UpsertTimer("ils-reminder", T0, intervalMinutes: 15, recurringIntervalMinutes: 30, isRunning: true);

        var timer = incident.FindTimer("ils-reminder");
        Assert.NotNull(timer);
        Assert.Equal(T0, timer!.CycleAnchor);
        Assert.Equal(15, timer.IntervalMinutes);
        Assert.Equal(30, timer.RecurringIntervalMinutes);
        Assert.True(timer.IsRunning);
        Assert.Null(incident.FindTimer("nope"));
    }

    [Fact]
    public void UpsertTimer_replaces_the_prior_state_for_the_same_key()
    {
        var incident = NewIncident();
        incident.UpsertTimer("ils-reminder", T0, 15, 30, true);

        incident.UpsertTimer("ils-reminder", T0.AddMinutes(15), 30, 30, true);

        Assert.Single(incident.Timers);                       // not appended — replaced
        Assert.Equal(T0.AddMinutes(15), incident.FindTimer("ils-reminder")!.CycleAnchor);
        Assert.Equal(30, incident.FindTimer("ils-reminder")!.IntervalMinutes);
    }

    [Fact]
    public void UpsertTimer_on_a_closed_incident_is_rejected()
    {
        var incident = NewIncident();
        incident.Close(new FixedClock(T0.AddHours(1)), new SessionOperator("Müller", "FFB 12/1"));

        Assert.Throws<IncidentClosedException>(() => incident.UpsertTimer("ils-reminder", T0, 15, 30, true));
    }

    [Fact]
    public void Rehydrate_carries_timers()
    {
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), T0, IncidentState.Open, null, null, null, null, null, null, null,
            Array.Empty<ChecklistItem>(), Array.Empty<ChecklistItem>(), Array.Empty<Etb.EtbEntry>(),
            Array.Empty<RoleAssignment>(), Array.Empty<ForceUnit>(),
            Array.Empty<Atemschutz.AtemschutzTrupp>(), Array.Empty<AuditEvent>(),
            new[] { new IncidentTimerState("ils-reminder", T0, 15, 30, true) },
            Array.Empty<Files.IncidentFile>());

        Assert.Equal("ils-reminder", Assert.Single(incident.Timers).Key);
    }
}
