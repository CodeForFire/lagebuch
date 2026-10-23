using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.Tests;

public class NodeChoicesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly SessionOperator Op = new("Mustermann");

    private static Incident NewIncident()
    {
        var session = TestSession.StartNew(
            new FakeStore(),
            new FixedClock(T0),
            Op,
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return session.Incident;
    }

    private static CommandNode Add(Incident incident, CommandNodeKind kind, string name, Guid? parent = null) =>
        incident.AddCommandNode(new FixedClock(T0), Op, kind, name, parent);

    [Fact]
    public void The_unplaced_entry_comes_first_and_is_current_for_no_node()
    {
        var incident = NewIncident();

        var choices = NodeChoices.Build(incident, null, _ => { });

        var only = Assert.Single(choices);
        Assert.Null(only.NodeId);
        Assert.Equal(NodeChoices.UnplacedLabel, only.Label);
        Assert.True(only.IsCurrent);
    }

    [Fact]
    public void Live_nodes_follow_in_reporting_order_with_their_labels()
    {
        var incident = NewIncident();
        var abschnitt2 = Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 2");
        var abschnitt1 = Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var nord = Add(incident, CommandNodeKind.Unterabschnitt, "Nord", abschnitt1.Id);
        var bereitstellung = Add(incident, CommandNodeKind.Bereitstellung, "Bereitstellung");

        var choices = NodeChoices.Build(incident, nord.Id, _ => { });

        var expectedOrder = ReportingLine.Of(incident)
            .Where(e => e.Node is not null && e.Node.Kind != CommandNodeKind.Partner)
            .Select(e => e.Node!.Id)
            .ToList();
        Assert.Equal(expectedOrder, choices.Skip(1).Select(c => c.NodeId!.Value));
        Assert.Equal(
            new[] { NodeChoices.UnplacedLabel, "Bereitstellung", "Abschnitt 2", "Abschnitt 1", "Abschnitt 1 / Nord" }
                .Order(StringComparer.Ordinal),
            choices.Select(c => c.Label).Order(StringComparer.Ordinal));
        Assert.Equal("Abschnitt 1 / Nord", choices.Single(c => c.NodeId == nord.Id).Label);
        Assert.Equal(nord.Id, Assert.Single(choices, c => c.IsCurrent).NodeId);
        Assert.Contains(choices, c => c.NodeId == abschnitt2.Id);
        Assert.Contains(choices, c => c.NodeId == bereitstellung.Id);
    }

    [Fact]
    public void An_Unterabschnitt_directly_follows_its_Abschnitt()
    {
        var incident = NewIncident();
        var a1 = Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 1");
        Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 2");
        Add(incident, CommandNodeKind.Unterabschnitt, "Nord", a1.Id);

        var labels = NodeChoices.Build(incident, null, _ => { }).Select(c => c.Label).ToList();

        Assert.Equal(
            new[] { NodeChoices.UnplacedLabel, "Abschnitt 1", "Abschnitt 1 / Nord", "Abschnitt 2" },
            labels);
    }

    [Fact]
    public void Partner_and_dissolved_nodes_are_not_offered()
    {
        var incident = NewIncident();
        Add(incident, CommandNodeKind.Partner, "Polizei");
        var gone = Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 9");
        incident.DissolveCommandNode(new FixedClock(T0), Op, gone.Id);
        Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 1");

        var labels = NodeChoices.Build(incident, null, _ => { }).Select(c => c.Label).ToList();

        Assert.Equal(new[] { NodeChoices.UnplacedLabel, "Abschnitt 1" }, labels);
    }

    [Fact]
    public void A_unit_in_a_Partner_a_dissolved_or_a_missing_node_counts_as_unplaced()
    {
        var incident = NewIncident();
        var partner = Add(incident, CommandNodeKind.Partner, "Polizei");
        var gone = Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 9");
        incident.DissolveCommandNode(new FixedClock(T0), Op, gone.Id);
        Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 1");

        foreach (Guid? hostile in new Guid?[] { partner.Id, gone.Id, Guid.NewGuid() })
        {
            var choices = NodeChoices.Build(incident, hostile, _ => { });

            var current = Assert.Single(choices, c => c.IsCurrent);
            Assert.Null(current.NodeId);
        }
    }

    [Fact]
    public void Selecting_an_entry_reports_its_node_id()
    {
        var incident = NewIncident();
        var a1 = Add(incident, CommandNodeKind.Abschnitt, "Abschnitt 1");
        var picked = new List<Guid?>();

        var choices = NodeChoices.Build(incident, null, picked.Add);
        choices.Single(c => c.NodeId == a1.Id).SelectCommand.Execute(null);
        choices[0].SelectCommand.Execute(null);

        Assert.Equal(new Guid?[] { a1.Id, null }, picked);
    }
}
