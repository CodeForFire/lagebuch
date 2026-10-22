using LageBuch.Domain.Involved;

namespace LageBuch.Domain.Tests;

/// <summary>
/// Where a Beteiligte/r is now, after the evacuation: free text with the time it was last changed,
/// so a question at the door ("where is Frau Beispiel?") has an answer. Silent like every other
/// Beteiligte change: a third party's whereabouts stay out of the ETB.
/// </summary>
public class InvolvedPartyWhereaboutsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op, InvolvedParty Party) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        return (incident, clock, op, party);
    }

    [Fact]
    public void A_aufenthaltsort_is_recorded_with_the_time_and_without_an_etb_line()
    {
        var (incident, clock, _, party) = NewIncident();
        var entries = incident.Journal.Count;
        clock.Now = T0.AddMinutes(12);

        var updated = incident.SetInvolvedPartyWhereabouts(clock, party.Id, "  bei Nachbarin Frau Meier ");

        Assert.Equal("bei Nachbarin Frau Meier", updated.Whereabouts);
        Assert.Equal(T0.AddMinutes(12), updated.WhereaboutsAt);
        Assert.Same(updated, incident.InvolvedParties.Single());
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void A_new_aufenthaltsort_moves_the_time_and_the_same_one_again_does_not()
    {
        var (incident, clock, _, party) = NewIncident();
        incident.SetInvolvedPartyWhereabouts(clock, party.Id, "bei Nachbarin Frau Meier");

        clock.Now = T0.AddMinutes(30);
        var moved = incident.SetInvolvedPartyWhereabouts(clock, party.Id, "im RTW Rotkreuz Musterstadt 71/1");
        Assert.Equal("im RTW Rotkreuz Musterstadt 71/1", moved.Whereabouts);
        Assert.Equal(T0.AddMinutes(30), moved.WhereaboutsAt);

        clock.Now = T0.AddMinutes(45);
        var resaved = incident.SetInvolvedPartyWhereabouts(clock, party.Id, "im RTW Rotkreuz Musterstadt 71/1 ");
        Assert.Equal(T0.AddMinutes(30), resaved.WhereaboutsAt);
    }

    [Fact]
    public void A_blank_aufenthaltsort_clears_the_text_and_the_time()
    {
        var (incident, clock, _, party) = NewIncident();
        incident.SetInvolvedPartyWhereabouts(clock, party.Id, "bei Nachbarin Frau Meier");

        var cleared = incident.SetInvolvedPartyWhereabouts(clock, party.Id, "   ");

        Assert.Null(cleared.Whereabouts);
        Assert.Null(cleared.WhereaboutsAt);
    }

    [Fact]
    public void A_correction_of_name_or_phone_keeps_the_aufenthaltsort()
    {
        var (incident, clock, _, party) = NewIncident();
        incident.SetInvolvedPartyWhereabouts(clock, party.Id, "bei Nachbarin Frau Meier");

        var updated = incident.UpdateInvolvedParty(party.Id, "Erika Beispiel-Muster", "0171 2345678", null);

        Assert.Equal("bei Nachbarin Frau Meier", updated.Whereabouts);
        Assert.Equal(T0, updated.WhereaboutsAt);
    }

    [Fact]
    public void A_aufenthaltsort_over_the_limit_is_refused_with_the_field_named()
    {
        var (incident, clock, _, party) = NewIncident();

        var ex = Assert.Throws<ArgumentException>(() =>
            incident.SetInvolvedPartyWhereabouts(clock, party.Id, new string('x', InvolvedParty.MaxWhereaboutsLength + 1)));

        Assert.Contains("Aufenthaltsort", ex.Message, StringComparison.Ordinal);
        Assert.Null(incident.InvolvedParties.Single().Whereabouts);
    }

    [Fact]
    public void A_aufenthaltsort_at_the_limit_is_taken()
    {
        var (incident, clock, _, party) = NewIncident();

        var updated = incident.SetInvolvedPartyWhereabouts(clock, party.Id, new string('x', InvolvedParty.MaxWhereaboutsLength));

        Assert.Equal(InvolvedParty.MaxWhereaboutsLength, updated.Whereabouts?.Length);
    }

    [Fact]
    public void A_aufenthaltsort_for_an_unknown_beteiligte_is_refused()
    {
        var (incident, clock, _, _) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.SetInvolvedPartyWhereabouts(clock, Guid.NewGuid(), "bei Nachbarin Frau Meier"));
    }

    [Fact]
    public void A_aufenthaltsort_cannot_be_set_on_a_closed_incident()
    {
        var (incident, clock, op, party) = NewIncident();
        incident.Close(clock, op);

        Assert.ThrowsAny<InvalidOperationException>(() => incident.SetInvolvedPartyWhereabouts(clock, party.Id, "bei Nachbarin Frau Meier"));
    }
}
