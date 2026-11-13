using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class FunkCheckTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 14, 0, 0, TimeSpan.FromHours(1));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident(Funkgruppen? post = null)
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        var incident = Incident.Start(clock, op);
        if (post is not null)
        {
            incident.UpdateLeadershipPost(clock, op, null, null, null, post);
        }

        return (incident, clock, op);
    }

    private static FunkCheckResult For(Incident incident, CommandNode node) =>
        Assert.Single(FunkCheck.Of(incident), r => r.NodeId == node.Id);

    [Fact]
    public void An_abschnitt_sharing_the_tmo_but_not_the_dmo_of_the_einsatzleitung_is_a_dmo_mismatch()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW_MUSTER (KW: 1000)"], ["301_F"]));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW_MUSTER (KW: 1000)", "FW_NORD"], ["305_F"]));

        var result = For(incident, nord);

        Assert.Equal(FunkCheck.EinsatzleitungLabel, result.ParentLabel);
        Assert.Equal(["FW_MUSTER (KW: 1000)"], result.Tmo.Shared);
        Assert.False(result.Tmo.IsMismatch);
        Assert.True(result.Tmo.IsShared("FW_MUSTER (KW: 1000)"));
        Assert.Null(result.Tmo.IsShared("FW_NORD"));
        Assert.True(result.Dmo.IsMismatch);
        Assert.False(result.Dmo.IsShared("305_F"));
    }

    [Fact]
    public void A_second_funkgruppe_of_a_kind_that_already_shares_one_is_not_marked()
    {
        // #887: one Funkgruppe in common is enough to talk to the level above, so the other one
        // of the same kind is neither shared nor a warning.
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], null));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 2000", "fw  1000"], null));

        var result = For(incident, nord);

        Assert.False(result.Tmo.IsMismatch);
        Assert.True(result.Tmo.IsShared("fw  1000"));
        Assert.Null(result.Tmo.IsShared("FW 2000"));
    }

    [Fact]
    public void Every_funkgruppe_of_a_kind_without_one_in_common_is_marked_not_shared()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], null));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 2000", "FW 3000"], null));

        var result = For(incident, nord);

        Assert.True(result.Tmo.IsMismatch);
        Assert.False(result.Tmo.IsShared("FW 2000"));
        Assert.False(result.Tmo.IsShared("FW 3000"));
    }

    [Fact]
    public void A_kind_both_sides_left_empty_is_not_checked()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], null));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 1000"], null));

        var result = For(incident, nord);

        Assert.False(result.Dmo.IsChecked);
        Assert.False(result.Dmo.IsMismatch);
        Assert.Null(result.Dmo.IsShared("305_F"));
    }

    [Fact]
    public void A_kind_only_the_node_has_is_a_mismatch()
    {
        // v0.10.0-beta.1 feedback: a DMO Funkgruppe the Einsatzleitung has not entered is no
        // shared one, so the node cannot reach it on DMO.
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], null));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 1000"], ["305_F"]));

        var result = For(incident, nord);

        Assert.True(result.Dmo.IsChecked);
        Assert.True(result.Dmo.IsMismatch);
        Assert.False(result.Dmo.IsShared("305_F"));
        Assert.False(result.Tmo.IsMismatch);
    }

    [Fact]
    public void A_kind_only_the_level_above_has_is_a_mismatch()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], ["301_F"]));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 1000"], null));

        var result = For(incident, nord);

        Assert.True(result.Dmo.IsMismatch);
        Assert.Empty(result.Dmo.Shared);
    }

    [Fact]
    public void The_reported_partner_on_another_tmo_and_no_dmo_on_either_side_is_a_tmo_mismatch_only()
    {
        // The report from v0.10.0-beta.1, literally: EL Fw on TMO A, EL RD on TMO B, no DMO anywhere.
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW_A"], null));
        var rd = incident.AddCommandNode(
            clock, op, CommandNodeKind.Partner, "Rettungsdienst", funkgruppen: new Funkgruppen(["RD_B"], null));

        var result = For(incident, rd);
        var el = FunkCheck.OfEinsatzleitung(incident);

        Assert.True(result.Tmo.IsMismatch);
        Assert.False(result.Dmo.IsChecked);
        Assert.True(el.Tmo.IsMismatch);
        Assert.False(el.Tmo.IsShared("FW_A"));
        Assert.Equal(["Rettungsdienst"], el.TmoMismatchWith);
        Assert.False(el.Dmo.IsChecked);
        Assert.Empty(el.DmoMismatchWith);
    }

    [Fact]
    public void An_einsatzleitung_funkgruppe_a_partner_shares_is_shared()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000", "fw 2000"], ["301_F"]));
        incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Rettungsdienst", funkgruppen: new Funkgruppen(["FW 2000"], ["301_F"]));
        incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei", funkgruppen: new Funkgruppen(["POL 1"], ["301_F"]));

        var el = FunkCheck.OfEinsatzleitung(incident);

        Assert.Equal(["fw 2000"], el.Tmo.Shared);
        Assert.True(el.Tmo.IsShared("fw 2000"));
        Assert.Null(el.Tmo.IsShared("FW 1000"));
        Assert.Equal(["Polizei"], el.TmoMismatchWith);
        Assert.Equal(["301_F"], el.Dmo.Shared);
        Assert.Empty(el.DmoMismatchWith);
    }

    [Fact]
    public void The_einsatzleitung_is_checked_against_live_partners_only_not_abschnitte()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], ["301_F"]));
        incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 2000"], ["305_F"]));
        var rd = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Rettungsdienst", funkgruppen: new Funkgruppen(["RD 1"], null));
        incident.DissolveCommandNode(clock, op, rd.Id);

        var el = FunkCheck.OfEinsatzleitung(incident);

        Assert.False(el.Tmo.IsChecked);
        Assert.False(el.Dmo.IsChecked);
        Assert.Empty(el.TmoMismatchWith);
        Assert.Empty(el.DmoMismatchWith);
    }

    [Fact]
    public void An_unterabschnitt_is_checked_against_its_abschnitt_not_the_einsatzleitung()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], ["301_F"]));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(["FW 1000"], ["305_F"]));
        var west = incident.AddCommandNode(
            clock, op, CommandNodeKind.Unterabschnitt, "West", nord.Id, funkgruppen: new Funkgruppen(["FW 2000"], ["305_F"]));

        var result = For(incident, west);

        Assert.Equal("Abschnitt Nord", result.ParentLabel);
        Assert.True(result.Tmo.IsMismatch);
        Assert.Equal(["305_F"], result.Dmo.Shared);
    }

    [Fact]
    public void A_partner_and_a_bereitstellung_are_checked_against_the_einsatzleitung()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], ["301_F"]));
        var polizei = incident.AddCommandNode(
            clock, op, CommandNodeKind.Partner, "Polizei", funkgruppen: new Funkgruppen(["POL 1"], ["301_F"]));
        var bereitstellung = incident.AddCommandNode(
            clock, op, CommandNodeKind.Bereitstellung, "B-Raum", funkgruppen: new Funkgruppen(["FW 1000"], null));

        var partner = For(incident, polizei);
        var raum = For(incident, bereitstellung);

        Assert.Equal(FunkCheck.EinsatzleitungLabel, partner.ParentLabel);
        Assert.True(partner.Tmo.IsMismatch);
        Assert.False(partner.Dmo.IsMismatch);
        Assert.Equal(FunkCheck.EinsatzleitungLabel, raum.ParentLabel);
        Assert.False(raum.Tmo.IsMismatch);
    }

    [Fact]
    public void An_unterabschnitt_whose_abschnitt_is_dissolved_is_checked_against_the_einsatzleitung()
    {
        var nord = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, T0, new Funkgruppen(["FW 2000"], null));
        var west = CommandNode.Rehydrate(
            Guid.NewGuid(), CommandNodeKind.Unterabschnitt, "West", nord.Id, null, null, null, null, null, null, new Funkgruppen(["FW 1000"], null));
        var orphaned = Incident.Rehydrate(
            Guid.NewGuid(),
            T0,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ChecklistList>(),
            Array.Empty<Etb.EtbEntry>(),
            Array.Empty<RoleAssignment>(),
            Array.Empty<ForceUnit>(),
            Array.Empty<Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<Time.IncidentTimerState>(),
            Array.Empty<Files.IncidentFile>(),
            Array.Empty<Tasks.IncidentTask>(),
            Array.Empty<CoMeasurement.Building>(),
            Array.Empty<CoMeasurement.Dwelling>(),
            Array.Empty<Involved.InvolvedParty>(),
            commandNodes: [nord, west],
            leadershipPost: new LeadershipPost(null, null, null, new Funkgruppen(["FW 1000"], null)));

        var result = Assert.Single(FunkCheck.Of(orphaned));

        Assert.Equal(west.Id, result.NodeId);
        Assert.Equal(FunkCheck.EinsatzleitungLabel, result.ParentLabel);
        Assert.False(result.Tmo.IsMismatch);
    }

    [Theory]
    [InlineData("FW_MUSTER 1000", "fw_muster   1000")]
    [InlineData(" 301_F ", "301_f")]
    [InlineData("FW\t1000", "FW 1000")]
    public void Funkgruppen_match_ignoring_case_and_whitespace(string a, string b) =>
        Assert.True(FunkCheck.SameGroup(a, b));

    [Theory]
    [InlineData("FW 1000", "FW 1001")]
    [InlineData("FW1000", "FW 1000")]
    public void Different_funkgruppen_do_not_match(string a, string b) =>
        Assert.False(FunkCheck.SameGroup(a, b));
}
