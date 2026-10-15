using LageBuch.Domain.Etb;
using LageBuch.Domain.SituationReports;

namespace LageBuch.Domain.Tests;

public class SituationReportAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 27, 14, 0, 0, TimeSpan.FromHours(2));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "Florian Musterstadt 11/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static SituationReport Draft(Incident incident, FixedClock clock, SessionOperator op, string situation = "Schuppen im Vollbrand") =>
        incident.AddSituationReport(
            clock, op, "Florian Musterstadt 11/1", "Blumenstraße 13", situation, "Brandbekämpfung mit einem C-Hohlstrahlrohr", "1 Trupp unter PA im Einsatz", null);

    [Fact]
    public void AddSituationReport_starts_an_unnumbered_draft_without_an_etb_line()
    {
        var (incident, clock, op) = NewIncident();

        var draft = Draft(incident, clock, op);

        Assert.Same(draft, Assert.Single(incident.SituationReports));
        Assert.Null(draft.Number);
        Assert.False(draft.IsSent);
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void SendSituationReport_numbers_the_report_and_writes_an_outgoing_etb_entry_to_the_leitstelle()
    {
        var (incident, clock, op) = NewIncident();
        var draft = Draft(incident, clock, op);
        clock.Now = T0.AddMinutes(5);

        var sent = incident.SendSituationReport(clock, op, draft.Id, "ILS");

        Assert.Equal(1, sent.Number);
        Assert.Equal(T0.AddMinutes(5), sent.SentAt);
        Assert.Equal(op.Display, sent.SentBy);
        var entry = incident.Journal[^1];
        Assert.Equal(EtbDirection.Outgoing, entry.Direction);
        Assert.Equal("Florian Musterstadt 11/1", entry.From);
        Assert.Equal("ILS", entry.To);
        Assert.Equal(
            "Lagemeldung Nr. 1: Blumenstraße 13 · Schuppen im Vollbrand · Brandbekämpfung mit einem C-Hohlstrahlrohr · 1 Trupp unter PA im Einsatz",
            entry.Text);
    }

    [Fact]
    public void Numbers_have_no_gap_when_a_draft_is_discarded()
    {
        var (incident, clock, op) = NewIncident();
        incident.SendSituationReport(clock, op, Draft(incident, clock, op).Id, "ILS");
        incident.RemoveSituationReport(Draft(incident, clock, op).Id);

        var third = incident.SendSituationReport(clock, op, Draft(incident, clock, op).Id, "ILS");

        Assert.Equal(2, third.Number);
    }

    [Fact]
    public void A_sent_report_can_neither_be_changed_nor_removed_nor_sent_again()
    {
        var (incident, clock, op) = NewIncident();
        var sent = incident.SendSituationReport(clock, op, Draft(incident, clock, op).Id, "ILS");

        Assert.Throws<InvalidOperationException>(() =>
            incident.UpdateSituationReport(sent.Id, null, null, "anders", null, null, null));
        Assert.Throws<InvalidOperationException>(() => incident.RemoveSituationReport(sent.Id));
        Assert.Throws<InvalidOperationException>(() => incident.SendSituationReport(clock, op, sent.Id, "ILS"));
    }

    [Fact]
    public void UpdateSituationReport_replaces_the_draft_in_place()
    {
        var (incident, clock, op) = NewIncident();
        var draft = Draft(incident, clock, op);

        var updated = incident.UpdateSituationReport(draft.Id, "ELW 1", "Blumenstraße 13", "Feuer aus", null, null, "1 RTW");

        Assert.Equal(draft.Id, updated.Id);
        Assert.Same(updated, Assert.Single(incident.SituationReports));
        Assert.Equal("Blumenstraße 13 · Feuer aus · Nachforderung: 1 RTW", updated.Funkspruch);
    }

    [Fact]
    public void An_empty_report_cannot_be_sent()
    {
        var (incident, clock, op) = NewIncident();
        var empty = incident.AddSituationReport(clock, op, "ELW 1", null, null, null, null, null);

        Assert.Throws<ArgumentException>(() => incident.SendSituationReport(clock, op, empty.Id, "ILS"));
        Assert.Null(incident.SituationReports[0].Number);
        Assert.Single(incident.Journal);
    }

    [Fact]
    public void An_unknown_report_id_throws()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<KeyNotFoundException>(() => incident.SendSituationReport(clock, op, Guid.NewGuid(), "ILS"));
    }

    [Fact]
    public void A_closed_incident_takes_no_situation_reports()
    {
        var (incident, clock, op) = NewIncident();
        var draft = Draft(incident, clock, op);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => Draft(incident, clock, op));
        Assert.Throws<IncidentClosedException>(() => incident.SendSituationReport(clock, op, draft.Id, "ILS"));
    }
}
