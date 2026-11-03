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
        Assert.False(result.Tmo.IsShared("FW_NORD"));
        Assert.True(result.Dmo.IsMismatch);
        Assert.False(result.Dmo.IsShared("305_F"));
    }

    [Fact]
    public void A_kind_either_side_left_empty_is_not_checked()
    {
        var (incident, clock, op) = NewIncident(new Funkgruppen(["FW 1000"], null));
        var nord = incident.AddCommandNode(
            clock, op, CommandNodeKind.Abschnitt, "Nord", funkgruppen: new Funkgruppen(null, ["305_F"]));

        var result = For(incident, nord);

        Assert.False(result.Tmo.IsChecked);
        Assert.False(result.Dmo.IsChecked);
        Assert.False(result.Tmo.IsMismatch);
        Assert.False(result.Dmo.IsMismatch);
        Assert.Null(result.Dmo.IsShared("305_F"));
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
