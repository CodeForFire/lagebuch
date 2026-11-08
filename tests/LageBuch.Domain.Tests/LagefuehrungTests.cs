using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;

namespace LageBuch.Domain.Tests;

public class LagefuehrungTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 8, 14, 0, 0, TimeSpan.FromHours(1));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static string[] NewLines(Incident incident, int before) =>
        [.. incident.Journal.Skip(before).Select(e => e.Text)];

    [Fact]
    public void The_Feuerwehr_holds_the_Lagefuehrung_until_it_is_handed_on()
    {
        var (incident, _, _) = NewIncident();

        Assert.Null(incident.Lagefuehrung);
        Assert.Null(incident.LagefuehrungNodeId);
    }

    [Fact]
    public void Handing_the_Lagefuehrung_to_a_partner_and_back_is_logged_each_time()
    {
        var (incident, clock, op) = NewIncident();
        var wasserrettung = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Wasserrettung");
        var before = incident.Journal.Count;

        incident.SetLagefuehrung(clock, op, wasserrettung.Id);
        Assert.Equal(wasserrettung.Id, incident.Lagefuehrung?.Id);

        incident.SetLagefuehrung(clock, op, null);
        Assert.Null(incident.Lagefuehrung);

        Assert.Equal(
            new[] { "Lageführung: Einsatzleitung Wasserrettung", "Lageführung: Einsatzleitung Feuerwehr" },
            NewLines(incident, before));
    }

    [Fact]
    public void Handing_the_Lagefuehrung_to_whoever_holds_it_writes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        var polizei = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");
        incident.SetLagefuehrung(clock, op, polizei.Id);
        var before = incident.Journal.Count;

        incident.SetLagefuehrung(clock, op, polizei.Id);
        var feuerwehr = NewIncident();
        feuerwehr.Incident.SetLagefuehrung(feuerwehr.Clock, feuerwehr.Op, null);

        Assert.Equal(before, incident.Journal.Count);
        Assert.Single(feuerwehr.Incident.Journal);
    }

    [Fact]
    public void Only_a_live_partner_can_hold_the_Lagefuehrung()
    {
        var (incident, clock, op) = NewIncident();
        var abschnitt = incident.AddCommandNode(clock, op, CommandNodeKind.Abschnitt, "Nord");
        var polizei = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");
        incident.DissolveCommandNode(clock, op, polizei.Id);

        Assert.Throws<InvalidOperationException>(() => incident.SetLagefuehrung(clock, op, abschnitt.Id));
        Assert.Throws<InvalidOperationException>(() => incident.SetLagefuehrung(clock, op, polizei.Id));
        Assert.Throws<InvalidOperationException>(() => incident.SetLagefuehrung(clock, op, Guid.NewGuid()));
        Assert.Null(incident.Lagefuehrung);
    }

    [Fact]
    public void Dissolving_the_partner_that_holds_the_Lagefuehrung_gives_it_back_to_the_Feuerwehr()
    {
        var (incident, clock, op) = NewIncident();
        var polizei = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");
        incident.SetLagefuehrung(clock, op, polizei.Id);
        var before = incident.Journal.Count;

        incident.DissolveCommandNode(clock, op, polizei.Id);

        Assert.Null(incident.Lagefuehrung);
        Assert.Null(incident.LagefuehrungNodeId);
        Assert.Equal(
            new[] { "Einsatzleitung aufgelöst: Polizei", "Lageführung: Einsatzleitung Feuerwehr" },
            NewLines(incident, before));
    }

    [Fact]
    public void Dissolving_another_partner_leaves_the_Lagefuehrung_alone()
    {
        var (incident, clock, op) = NewIncident();
        var polizei = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Polizei");
        var rd = incident.AddCommandNode(clock, op, CommandNodeKind.Partner, "Rettungsdienst");
        incident.SetLagefuehrung(clock, op, polizei.Id);
        var before = incident.Journal.Count;

        incident.DissolveCommandNode(clock, op, rd.Id);

        Assert.Equal(polizei.Id, incident.Lagefuehrung?.Id);
        Assert.Single(NewLines(incident, before));
    }

    [Fact]
    public void A_stored_id_that_names_no_live_partner_reads_as_the_Feuerwehr()
    {
        var abschnittId = Guid.NewGuid();
        var abschnitt = CommandNode.Rehydrate(abschnittId, CommandNodeKind.Abschnitt, "Nord", null, null, null, null, null, null, null);

        var pointingAtAbschnitt = Rehydrated([abschnitt], lagefuehrungNodeId: abschnittId);
        var pointingNowhere = Rehydrated([], lagefuehrungNodeId: Guid.NewGuid());

        Assert.Null(pointingAtAbschnitt.Lagefuehrung);
        Assert.Null(pointingNowhere.Lagefuehrung);
    }

    [Fact]
    public void Rehydrate_restores_the_Lagefuehrung_and_the_channel()
    {
        var partnerId = Guid.NewGuid();
        var partner = CommandNode.Rehydrate(partnerId, CommandNodeKind.Partner, "Wasserrettung", null, null, null, null, null, null, null);

        var incident = Rehydrated([partner], " TMO WR_FFB ", partnerId);

        Assert.Equal(partnerId, incident.Lagefuehrung?.Id);
        Assert.Equal("TMO WR_FFB", incident.DispatchChannel);
    }

    [Fact]
    public void Rehydrate_drops_a_channel_over_the_cap_instead_of_throwing()
    {
        var incident = Rehydrated([], new string('x', CommandNode.MaxTextLength + 1));

        Assert.Null(incident.DispatchChannel);
    }

    [Fact]
    public void Setting_or_changing_the_channel_is_logged_and_clearing_it_is_not()
    {
        var (incident, clock, op) = NewIncident();
        var before = incident.Journal.Count;

        Assert.Equal("TMO WR_FFB", incident.SetDispatchChannel(clock, op, "  TMO WR_FFB "));
        incident.SetDispatchChannel(clock, op, "TMO WR_FFB");
        incident.SetDispatchChannel(clock, op, "TMO FW_FFB 1000");
        incident.SetDispatchChannel(clock, op, "   ");

        Assert.Null(incident.DispatchChannel);
        Assert.Equal(
            new[] { "Kanal zur Leitstelle: TMO WR_FFB", "Kanal zur Leitstelle: TMO FW_FFB 1000" },
            NewLines(incident, before));
    }

    [Fact]
    public void A_channel_over_the_cap_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        Assert.Throws<ArgumentException>(
            () => incident.SetDispatchChannel(clock, op, new string('x', CommandNode.MaxTextLength + 1)));
        Assert.Null(incident.DispatchChannel);
    }

    [Fact]
    public void A_closed_Einsatz_refuses_both()
    {
        var (incident, clock, op) = NewIncident();
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.SetDispatchChannel(clock, op, "TMO WR_FFB"));
        Assert.Throws<IncidentClosedException>(() => incident.SetLagefuehrung(clock, op, null));
    }

    private static Incident Rehydrated(
        IEnumerable<CommandNode> nodes, string? dispatchChannel = null, Guid? lagefuehrungNodeId = null) =>
        Incident.Rehydrate(
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
            Array.Empty<EtbEntry>(),
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
            commandNodes: nodes,
            dispatchChannel: dispatchChannel,
            lagefuehrungNodeId: lagefuehrungNodeId);
}
