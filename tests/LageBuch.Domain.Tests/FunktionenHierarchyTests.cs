using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

/// <summary>
/// Who reports to whom among the Funktionen (#703). As with the Sicherheitstrupp link, the domain
/// records the structure the Einsatzleitung chose and refuses only what can only ever be a bug: a
/// Funktion reporting to itself, a link to an assignment this incident does not have, and a loop.
/// </summary>
public class FunktionenHierarchyTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock, out SessionOperator op)
    {
        clock = new FixedClock(T0);
        op = new SessionOperator("Müller", "FFB 12/1");
        return Incident.Start(clock, op);
    }

    [Fact]
    public void A_funktion_reports_to_another_and_the_etb_says_so()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);
        var ea = incident.AssignRole(clock, op, "Abschnittsleiter", "Schmidt", from: clock.Now, section: "Nord");

        incident.SetRoleReportsTo(clock, op, ea.Id, el.Id);

        Assert.Equal(el.Id, incident.Roles.Single(r => r.Id == ea.Id).ReportsToId);
        Assert.Equal("Funktion Abschnittsleiter (Schmidt) meldet an EL (Huber)", incident.Journal[^1].Text);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void Clearing_the_link_is_recorded_too()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);
        var gf = incident.AssignRole(clock, op, "GF", "Schmidt", from: clock.Now);
        incident.SetRoleReportsTo(clock, op, gf.Id, el.Id);

        incident.SetRoleReportsTo(clock, op, gf.Id, null);

        Assert.Null(incident.Roles.Single(r => r.Id == gf.Id).ReportsToId);
        Assert.Equal("Funktion GF (Schmidt) meldet an: —", incident.Journal[^1].Text);
    }

    [Fact]
    public void Setting_the_same_link_again_writes_nothing()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);
        var gf = incident.AssignRole(clock, op, "GF", "Schmidt", from: clock.Now);
        incident.SetRoleReportsTo(clock, op, gf.Id, el.Id);
        var entries = incident.Journal.Count;

        incident.SetRoleReportsTo(clock, op, gf.Id, el.Id);

        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void A_funktion_cannot_report_to_itself()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);

        var error = Assert.Throws<ArgumentException>(() => incident.SetRoleReportsTo(clock, op, el.Id, el.Id));
        Assert.StartsWith("Eine Funktion kann nicht an sich selbst melden.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_to_an_assignment_the_incident_does_not_have_is_refused()
    {
        var incident = NewIncident(out var clock, out var op);
        var gf = incident.AssignRole(clock, op, "GF", "Schmidt", from: clock.Now);

        var unknownSuperior = Assert.Throws<ArgumentException>(() => incident.SetRoleReportsTo(clock, op, gf.Id, Guid.NewGuid()));
        var unknownSubordinate = Assert.Throws<ArgumentException>(() => incident.SetRoleReportsTo(clock, op, Guid.NewGuid(), gf.Id));
        Assert.StartsWith("Funktionszuweisung nicht gefunden.", unknownSuperior.Message, StringComparison.Ordinal);
        Assert.StartsWith("Funktionszuweisung nicht gefunden.", unknownSubordinate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_loop_is_refused()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);
        var ea = incident.AssignRole(clock, op, "Abschnittsleiter", "Schmidt", from: clock.Now);
        var gf = incident.AssignRole(clock, op, "GF", "Maier", from: clock.Now);
        incident.SetRoleReportsTo(clock, op, ea.Id, el.Id);
        incident.SetRoleReportsTo(clock, op, gf.Id, ea.Id);

        var error = Assert.Throws<InvalidOperationException>(() => incident.SetRoleReportsTo(clock, op, el.Id, gf.Id));

        Assert.Equal("EL (Huber) kann nicht an GF (Maier) melden: das ergäbe einen Kreis.", error.Message);
        Assert.Null(incident.Roles.Single(r => r.Id == el.Id).ReportsToId);
    }

    // A handover gives the Funktion a new assignment id. Whoever reported to the outgoing holder now
    // reports to the incoming one, and the incoming one reports where the outgoing one did.
    [Fact]
    public void A_handover_moves_the_subordinates_and_keeps_the_superior()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);
        var ea = incident.AssignRole(clock, op, "Abschnittsleiter", "Schmidt", from: clock.Now);
        var gf = incident.AssignRole(clock, op, "GF", "Maier", from: clock.Now);
        incident.SetRoleReportsTo(clock, op, ea.Id, el.Id);
        incident.SetRoleReportsTo(clock, op, gf.Id, ea.Id);

        clock.Now = clock.Now.AddMinutes(20);
        var next = incident.TransferRole(clock, op, ea.Id, "Wagner", null, null);

        Assert.Equal(el.Id, next.ReportsToId);
        Assert.Equal(next.Id, incident.Roles.Single(r => r.Id == gf.Id).ReportsToId);

        // The ended assignment keeps the link it had: the record of who reported where, then.
        Assert.Equal(el.Id, incident.Roles.Single(r => r.Id == ea.Id).ReportsToId);
    }

    [Fact]
    public void A_handover_leaves_an_ended_subordinate_pointing_where_it_did()
    {
        var incident = NewIncident(out var clock, out var op);
        var ea = incident.AssignRole(clock, op, "Abschnittsleiter", "Schmidt", from: clock.Now);
        var gf = incident.AssignRole(clock, op, "GF", "Maier", from: clock.Now);
        incident.SetRoleReportsTo(clock, op, gf.Id, ea.Id);
        clock.Now = clock.Now.AddMinutes(10);
        incident.EndRoleAssignment(gf.Id, clock.Now);

        clock.Now = clock.Now.AddMinutes(10);
        incident.TransferRole(clock, op, ea.Id, "Wagner", null, null);

        Assert.Equal(ea.Id, incident.Roles.Single(r => r.Id == gf.Id).ReportsToId);
    }

    [Fact]
    public void A_link_cannot_be_set_on_a_closed_incident()
    {
        var incident = NewIncident(out var clock, out var op);
        var el = incident.AssignRole(clock, op, "EL", "Huber", from: clock.Now);
        var gf = incident.AssignRole(clock, op, "GF", "Schmidt", from: clock.Now);
        incident.Close(clock, op);

        Assert.ThrowsAny<InvalidOperationException>(() => incident.SetRoleReportsTo(clock, op, gf.Id, el.Id));
    }
}
