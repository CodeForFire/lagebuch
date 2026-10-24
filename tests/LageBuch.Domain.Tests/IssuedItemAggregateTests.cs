namespace LageBuch.Domain.Tests;

public class IssuedItemAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    [Fact]
    public void IssueItem_appends_in_issue_order_without_an_etb_line()
    {
        var (incident, clock, op) = NewIncident();

        incident.IssueItem(clock, op, "Mobilteil 1", "Erika Beispiel", "0171 0000001", "THW", null);
        var second = incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);

        Assert.Equal(2, incident.IssuedItems.Count);
        Assert.Equal("Mobilteil 1", incident.IssuedItems[0].Device);
        Assert.Same(second, incident.IssuedItems[1]);

        // The recipient's name and phone number stay out of the ETB.
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void UpdateIssuedItem_replaces_the_entry_in_place()
    {
        var (incident, clock, op) = NewIncident();
        incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);
        var target = incident.IssueItem(clock, op, "Fernglas", "Erika Beispiel", null, null, null);

        var updated = incident.UpdateIssuedItem(target.Id, "Fernglas", "Erika Beispiel", "0171 0000002", "Polizei", null);

        Assert.Equal(target.Id, updated.Id);
        Assert.Same(updated, incident.IssuedItems[1]);
        Assert.Equal("0171 0000002", incident.IssuedItems[1].IssuedToPhone);
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void ReturnIssuedItem_stamps_the_clock_and_undo_reverts_it()
    {
        var (incident, clock, op) = NewIncident();
        var item = incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);
        clock.Now = T0.AddMinutes(30);

        var returned = incident.ReturnIssuedItem(clock, op, item.Id);

        Assert.Equal(T0.AddMinutes(30), returned.ReturnedAt);
        Assert.Same(returned, incident.IssuedItems[0]);

        var reopened = incident.UndoIssuedItemReturn(item.Id);

        Assert.True(reopened.IsOut);
        Assert.Same(reopened, incident.IssuedItems[0]);
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void ItemsOut_counts_only_what_has_not_come_back()
    {
        var (incident, clock, op) = NewIncident();
        var first = incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);
        incident.IssueItem(clock, op, "Fernglas", "Erika Beispiel", null, null, null);
        incident.IssueItem(clock, op, "GoPro", "Erika Beispiel", null, null, null);

        incident.ReturnIssuedItem(clock, op, first.Id);

        Assert.Equal(2, incident.ItemsOut);
    }

    [Fact]
    public void RemoveIssuedItem_takes_the_entry_out()
    {
        var (incident, clock, op) = NewIncident();
        var item = incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);

        incident.RemoveIssuedItem(item.Id);

        Assert.Empty(incident.IssuedItems);
    }

    [Fact]
    public void Operations_on_an_unknown_id_throw()
    {
        var (incident, clock, op) = NewIncident();
        var unknown = Guid.NewGuid();

        Assert.Throws<KeyNotFoundException>(() => incident.UpdateIssuedItem(unknown, "AED", "Max", null, null, null));
        Assert.Throws<KeyNotFoundException>(() => incident.ReturnIssuedItem(clock, op, unknown));
        Assert.Throws<KeyNotFoundException>(() => incident.UndoIssuedItemReturn(unknown));
        Assert.Throws<KeyNotFoundException>(() => incident.RemoveIssuedItem(unknown));
    }

    [Fact]
    public void A_closed_incident_takes_no_issue_changes()
    {
        var (incident, clock, op) = NewIncident();
        var item = incident.IssueItem(clock, op, "AED", "Max Beispiel", null, null, null);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.IssueItem(clock, op, "Fernglas", "Max", null, null, null));
        Assert.Throws<IncidentClosedException>(() => incident.UpdateIssuedItem(item.Id, "AED", "Max", null, null, null));
        Assert.Throws<IncidentClosedException>(() => incident.ReturnIssuedItem(clock, op, item.Id));
        Assert.Throws<IncidentClosedException>(() => incident.UndoIssuedItemReturn(item.Id));
        Assert.Throws<IncidentClosedException>(() => incident.RemoveIssuedItem(item.Id));
    }
}
