using CommunityToolkit.Mvvm.Input;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>Builds the entries of the Abschnitt picker shared by the Kräfte list and the Führung board.</summary>
public static class NodeChoices
{
    /// <summary>The label for "this unit sits in no Abschnitt".</summary>
    public const string UnplacedLabel = "— nicht zugeordnet —";

    /// <summary>
    /// "Nicht zugeordnet", then every live Bereitstellung, Abschnitt and Unterabschnitt in
    /// <see cref="ReportingLine"/> order. A <paramref name="currentNodeId"/> that names a node not
    /// offered (missing, dissolved, a Partner) leaves the unplaced entry current: such a unit
    /// counts as unplaced, matching <see cref="ReportingLine.UnplacedUnits"/>.
    /// </summary>
    public static IReadOnlyList<NodeChoice> Build(Incident incident, Guid? currentNodeId, Action<Guid?> onSelect)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(onSelect);

        var nodes = ReportingLine.Of(incident)
            .Select(e => e.Node)
            .OfType<CommandNode>()
            .Where(n => n.Kind != CommandNodeKind.Partner)
            .ToList();
        var current = nodes.Find(n => n.Id == currentNodeId)?.Id;

        var choices = new List<NodeChoice> { Choice(null, UnplacedLabel, current is null, onSelect) };
        foreach (var node in nodes)
        {
            choices.Add(Choice(node.Id, incident.NodeLabel(node.Id) ?? node.Name, node.Id == current, onSelect));
        }

        return choices;
    }

    /// <summary>
    /// What a closed picker says: the label of the node marked current, or <see cref="UnplacedLabel"/>
    /// when the unit counts as unplaced, so an empty picker still reads as one rather than a bare dash.
    /// </summary>
    public static string LabelOf(Incident incident, IReadOnlyList<NodeChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.NodeLabel(CurrentNodeId(choices)) ?? UnplacedLabel;
    }

    /// <summary>The node the entry marked current stands for; null when the unit counts as unplaced.</summary>
    public static Guid? CurrentNodeId(IReadOnlyList<NodeChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        return choices.FirstOrDefault(c => c.IsCurrent)?.NodeId;
    }

    /// <summary>
    /// Whether two pickers show the same thing: the same entries, labels and current mark. A picker's
    /// Flyout is rebuilt from its list, so an owner replaces the list only when this is false: swapping
    /// an items source under an open picker is what froze the Atemschutz picker in #408.
    /// </summary>
    public static bool SameEntries(IReadOnlyList<NodeChoice> a, IReadOnlyList<NodeChoice> b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return a.Select(Key).SequenceEqual(b.Select(Key));
    }

    private static (Guid? NodeId, string Label, bool IsCurrent) Key(NodeChoice c) => (c.NodeId, c.Label, c.IsCurrent);

    private static NodeChoice Choice(Guid? nodeId, string label, bool isCurrent, Action<Guid?> onSelect) =>
        new(nodeId, label, isCurrent, new RelayCommand(() => onSelect(nodeId)));
}

/// <summary>
/// One entry in an Abschnitt picker: a node a unit can sit in, or <see cref="NodeChoices.UnplacedLabel"/>
/// for none.
/// </summary>
/// <remarks>
/// A command per entry and not a ComboBox selection, for the reason documented on
/// <see cref="SafetyTruppChoice"/>: a selection bound to a setter that writes to the domain
/// re-enters through the incident's Changed event while the selection model is mid-update.
/// </remarks>
/// <param name="NodeId">The node, or null for "nicht zugeordnet".</param>
/// <param name="Label">What the node is called wherever it is shown.</param>
/// <param name="IsCurrent">Whether this is the entry in force, for the check mark.</param>
/// <param name="SelectCommand">Reports <paramref name="NodeId"/> to the picker's owner.</param>
public sealed record NodeChoice(Guid? NodeId, string Label, bool IsCurrent, IRelayCommand SelectCommand);
