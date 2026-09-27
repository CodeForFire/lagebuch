namespace LageBuch.Domain.Tests;

public class InvolvedPartyAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    [Fact]
    public void AddInvolvedParty_appends_in_creation_order_without_an_etb_line()
    {
        var (incident, clock, op) = NewIncident();

        incident.AddInvolvedParty(clock, op, "Erika Beispiel", "0171 0000001", "Hauseigentümerin");
        var second = incident.AddInvolvedParty(clock, op, "POK Mustermann", null, "Polizei vor Ort");

        Assert.Equal(2, incident.InvolvedParties.Count);
        Assert.Equal("Erika Beispiel", incident.InvolvedParties[0].Name);
        Assert.Same(second, incident.InvolvedParties[1]);

        // Personal data stays out of the ETB: only "Einsatz begonnen" is there.
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void UpdateInvolvedParty_replaces_the_entry_in_place_without_an_etb_line()
    {
        var (incident, clock, op) = NewIncident();
        incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        var target = incident.AddInvolvedParty(clock, op, "Max Beispiel", null, null);

        var updated = incident.UpdateInvolvedParty(target.Id, "Max Beispiel", "0171 0000002", "Fahrzeughalter");

        Assert.Equal(target.Id, updated.Id);
        Assert.Same(updated, incident.InvolvedParties[1]);
        Assert.Equal("0171 0000002", incident.InvolvedParties[1].Phone);
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void RemoveInvolvedParty_takes_the_entry_out_without_an_etb_line()
    {
        var (incident, clock, op) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);

        incident.RemoveInvolvedParty(party.Id);

        Assert.Empty(incident.InvolvedParties);
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void Update_and_remove_of_an_unknown_id_throw()
    {
        var (incident, _, _) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.UpdateInvolvedParty(Guid.NewGuid(), "X", null, null));
        Assert.Throws<KeyNotFoundException>(() => incident.RemoveInvolvedParty(Guid.NewGuid()));
    }

    [Fact]
    public void A_closed_incident_rejects_every_involved_party_mutation()
    {
        var (incident, clock, op) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AddInvolvedParty(clock, op, "X", null, null));
        Assert.Throws<IncidentClosedException>(() => incident.UpdateInvolvedParty(party.Id, "X", null, null));
        Assert.Throws<IncidentClosedException>(() => incident.RemoveInvolvedParty(party.Id));
    }

    [Fact]
    public void A_rejected_update_leaves_the_entry_untouched()
    {
        var (incident, clock, op) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);

        Assert.Throws<ArgumentException>(() => incident.UpdateInvolvedParty(party.Id, " ", null, null));

        Assert.Same(party, Assert.Single(incident.InvolvedParties));
    }

    [Fact]
    public void Rehydrate_round_trips_involved_parties_in_order()
    {
        var (seed, clock, op) = NewIncident();
        seed.AddInvolvedParty(clock, op, "Erika Beispiel", "0171 0000001", null);
        seed.AddInvolvedParty(clock, op, "Max Beispiel", null, "Halter");

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
            seed.Checklists,
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
            seed.InvolvedParties);

        Assert.Equal(seed.InvolvedParties, restored.InvolvedParties);
    }
}
