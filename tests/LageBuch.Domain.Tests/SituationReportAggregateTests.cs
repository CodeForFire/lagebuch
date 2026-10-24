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
        Assert.Equal(entry.Id, sent.EtbEntryId);
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

    [Fact]
    public void A_closed_incident_refuses_a_correction_a_discard_and_a_send_before_looking_at_the_draft()
    {
        var (incident, clock, op) = NewIncident();
        var draft = Draft(incident, clock, op);
        var empty = incident.AddSituationReport(clock, op, "ELW 1", null, null, null, null, null);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.UpdateSituationReport(draft.Id, null, null, "anders", null, null, null));
        Assert.Throws<IncidentClosedException>(() => incident.RemoveSituationReport(draft.Id));

        // Closed comes first: an empty draft or an unknown id must not decide the answer.
        Assert.Throws<IncidentClosedException>(() => incident.SendSituationReport(clock, op, empty.Id, "ILS"));
        Assert.Throws<IncidentClosedException>(() => incident.SendSituationReport(clock, op, Guid.NewGuid(), "ILS"));
        Assert.Equal(2, incident.SituationReports.Count);
    }

    [Fact]
    public void The_leitstelle_name_may_be_as_long_as_a_meldender_but_no_longer()
    {
        var (incident, clock, op) = NewIncident();
        var first = Draft(incident, clock, op);
        var second = Draft(incident, clock, op);
        var longest = new string('L', SituationReport.MaxReporterLength);

        var sent = incident.SendSituationReport(clock, op, first.Id, $"  {longest}  ");
        var error = Assert.Throws<ArgumentException>(() => incident.SendSituationReport(clock, op, second.Id, longest + "L"));

        Assert.Equal(longest, incident.Journal.Single(e => e.Id == sent.EtbEntryId).To);
        Assert.StartsWith("Der Name der Leitstelle ist zu lang.", error.Message, StringComparison.Ordinal);
        Assert.Equal("dispatchCentreName", error.ParamName);
    }

    [Fact]
    public void Each_refusal_says_why_in_german()
    {
        var (incident, clock, op) = NewIncident();
        var sent = Draft(incident, clock, op);
        incident.SendSituationReport(clock, op, sent.Id, "ILS");
        var empty = incident.AddSituationReport(clock, op, "ELW 1", null, null, null, null, null);
        var unknown = Guid.NewGuid();

        Assert.StartsWith(
            "Die Lagemeldung ist leer.",
            Assert.Throws<ArgumentException>(() => incident.SendSituationReport(clock, op, empty.Id, "ILS")).Message,
            StringComparison.Ordinal);
        Assert.Equal(
            $"Lagemeldung {unknown} nicht gefunden.",
            Assert.Throws<KeyNotFoundException>(() => incident.RemoveSituationReport(unknown)).Message);
        Assert.Equal(
            "Eine gesendete Lagemeldung kann nicht mehr geändert werden.",
            Assert.Throws<InvalidOperationException>(() => incident.RemoveSituationReport(sent.Id)).Message);
    }

    [Fact]
    public void Rehydrate_restores_the_lagemeldungen_in_order()
    {
        var (seed, clock, op) = NewIncident();
        var first = Draft(seed, clock, op);
        seed.SendSituationReport(clock, op, first.Id, "ILS");
        Draft(seed, clock, op, "Feuer aus");

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
            seed.InvolvedParties,
            situationReports: seed.SituationReports);

        Assert.Equal(seed.SituationReports, restored.SituationReports);
    }
}
