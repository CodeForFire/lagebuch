using LageBuch.Domain.CoMeasurement;

namespace LageBuch.Domain.Tests;

/// <summary>
/// A Beteiligte/r found in a Wohnung of the CO-Messung (#702). The link keeps the Wohnung's id and
/// a copy of where it was, so it still reads correctly once the Wohnung is gone from the building.
/// Silent like every other Beteiligte change: a third party's whereabouts stay out of the ETB.
/// </summary>
public class InvolvedPartyDwellingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op, Dwelling Dwelling) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        incident.AddCoBuilding(clock, op, "Hauptstr. 12", floorCount: 2, apartmentsPerFloor: 2);
        var dwelling = incident.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 2);
        return (incident, clock, op, dwelling);
    }

    [Fact]
    public void A_beteiligte_is_linked_to_a_wohnung_without_an_etb_line()
    {
        var (incident, clock, op, dwelling) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        var entries = incident.Journal.Count;

        var linked = incident.SetInvolvedPartyDwelling(party.Id, dwelling.Id);

        Assert.Equal(dwelling.Id, linked.DwellingId);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", linked.DwellingLabel);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", incident.DwellingLocationOf(linked));
        Assert.Same(linked, incident.InvolvedParties.Single());
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void The_beteiligte_found_in_a_wohnung_are_listed_in_the_order_they_were_recorded()
    {
        var (incident, clock, op, dwelling) = NewIncident();
        var erika = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.AddInvolvedParty(clock, op, "POK Mustermann", null, null);
        var max = incident.AddInvolvedParty(clock, op, "Max Beispiel", null, null);
        incident.SetInvolvedPartyDwelling(max.Id, dwelling.Id);
        incident.SetInvolvedPartyDwelling(erika.Id, dwelling.Id);

        Assert.Equal(["Erika Beispiel", "Max Beispiel"], incident.InvolvedPartiesIn(dwelling.Id).Select(p => p.Name));
    }

    [Fact]
    public void Clearing_the_link_drops_the_copied_location_too()
    {
        var (incident, clock, op, dwelling) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.SetInvolvedPartyDwelling(party.Id, dwelling.Id);

        var cleared = incident.SetInvolvedPartyDwelling(party.Id, null);

        Assert.Null(cleared.DwellingId);
        Assert.Null(cleared.DwellingLabel);
        Assert.Null(incident.DwellingLocationOf(cleared));
    }

    [Fact]
    public void A_wohnung_the_incident_does_not_have_is_refused()
    {
        var (incident, clock, op, _) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);

        var unknown = Guid.NewGuid();
        var error = Assert.Throws<KeyNotFoundException>(() => incident.SetInvolvedPartyDwelling(party.Id, unknown));
        Assert.Equal($"Wohnung {unknown} nicht gefunden.", error.Message);
        Assert.Throws<KeyNotFoundException>(() => incident.SetInvolvedPartyDwelling(Guid.NewGuid(), null));
        Assert.Null(incident.InvolvedParties.Single().DwellingId);
    }

    // The Wohnung can leave the building (fewer Wohnungen on the floor, the Haus removed). The
    // Beteiligte/r still says where they were found, from the copy taken when they were linked.
    [Fact]
    public void Once_the_wohnung_is_gone_the_copied_location_still_reads()
    {
        var (incident, clock, op, dwelling) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.SetInvolvedPartyDwelling(party.Id, dwelling.Id);

        incident.RemoveCoBuilding(clock, op, dwelling.BuildingId);

        var kept = incident.InvolvedParties.Single();
        Assert.Equal(dwelling.Id, kept.DwellingId);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", incident.DwellingLocationOf(kept));
    }

    [Fact]
    public void A_correction_of_name_or_phone_keeps_the_link()
    {
        var (incident, clock, op, dwelling) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.SetInvolvedPartyDwelling(party.Id, dwelling.Id);

        var updated = incident.UpdateInvolvedParty(party.Id, "Erika Beispiel", "0171 0000001", null);

        Assert.Equal(dwelling.Id, updated.DwellingId);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 2", updated.DwellingLabel);
    }

    [Fact]
    public void A_link_cannot_be_set_on_a_closed_incident()
    {
        var (incident, clock, op, dwelling) = NewIncident();
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);
        incident.Close(clock, op);

        Assert.ThrowsAny<InvalidOperationException>(() => incident.SetInvolvedPartyDwelling(party.Id, dwelling.Id));
    }
}
