namespace LageBuch.Domain.Tests;

public class WasserfoerderungAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 8, 30, 9, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock) NewIncident()
    {
        var clock = new FixedClock(T0);
        return (Incident.Start(clock, new SessionOperator("Müller")), clock);
    }

    [Fact]
    public void AddLeitung_numbers_sequentially_and_computes_the_plan()
    {
        var (incident, _) = NewIncident();

        incident.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 0);
        incident.AddWasserfoerderungLeitung(null, null, 400, 0);

        Assert.Equal(2, incident.Wasserfoerderung.Count);
        Assert.Equal(1, incident.Wasserfoerderung[0].Number);
        Assert.Equal(2, incident.Wasserfoerderung[1].Number);
        Assert.Equal("TLF 20/8", incident.Wasserfoerderung[0].Uebergabestelle);
        Assert.Equal(100, incident.Wasserfoerderung[0].HoseCount);
        Assert.Equal(3, incident.Wasserfoerderung[0].PumpCount);
        Assert.Equal(0, incident.Wasserfoerderung[1].PumpCount);
        Assert.Null(incident.Wasserfoerderung[1].Uebergabestelle);
    }

    [Fact]
    public void RemoveLeitung_removes_the_matching_leitung()
    {
        var (incident, _) = NewIncident();
        incident.AddWasserfoerderungLeitung(null, null, 2000, 0);
        var second = incident.AddWasserfoerderungLeitung(null, null, 400, 0);

        incident.RemoveWasserfoerderungLeitung(second.Id);

        Assert.Single(incident.Wasserfoerderung);
        Assert.Equal(1, incident.Wasserfoerderung[0].Number);
    }

    [Fact]
    public void RemoveLeitung_unknown_id_throws()
    {
        var (incident, _) = NewIncident();
        Assert.Throws<KeyNotFoundException>(() => incident.RemoveWasserfoerderungLeitung(Guid.NewGuid()));
    }

    [Fact]
    public void Closed_incident_rejects_leitung_mutations()
    {
        var (incident, clock) = NewIncident();
        incident.Close(clock, new SessionOperator("Müller"));

        Assert.Throws<IncidentClosedException>(() => incident.AddWasserfoerderungLeitung(null, null, 2000, 0));
        Assert.Throws<IncidentClosedException>(() => incident.RemoveWasserfoerderungLeitung(Guid.NewGuid()));
    }

    [Fact]
    public void Rehydrate_round_trips_leitungen_in_order()
    {
        var (seed, _) = NewIncident();
        seed.AddWasserfoerderungLeitung("TLF 20/8", "FFB 1/44/1", 2000, 100);
        seed.AddWasserfoerderungLeitung(null, null, 400, 0);

        var restored = Incident.Rehydrate(
            seed.Id,
            seed.StartedAt,
            seed.State,
            seed.IncidentNumber,
            seed.Keyword,
            seed.Street,
            seed.District,
            seed.Status,
            seed.ClosedAt,
            seed.ClosedBy,
            seed.ChecklistAufbau,
            seed.ChecklistAbbau,
            seed.Journal,
            seed.Roles,
            seed.Forces,
            seed.ScbaTrupps,
            seed.Audit,
            seed.Timers,
            seed.Files,
            seed.Tasks,
            seed.Buildings,
            seed.Dwellings,
            seed.Wasserfoerderung);

        Assert.Equal(2, restored.Wasserfoerderung.Count);
        Assert.Equal(4, restored.Wasserfoerderung[0].PumpCount);
        Assert.Equal(2, restored.Wasserfoerderung[1].Number);
        Assert.Equal(seed.Wasserfoerderung[0].PumpPositionsMeters, restored.Wasserfoerderung[0].PumpPositionsMeters);
    }
}