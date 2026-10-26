namespace LageBuch.Domain.Leadership;

/// <summary>
/// The rules for moving a node on the Führung board: reordering it among its siblings, hanging an
/// Unterabschnitt under another Abschnitt, and promoting or demoting between the two levels. One
/// place, so <see cref="Incident.MoveCommandNode"/> and the board's drag can never disagree.
/// </summary>
public static class CommandNodeMoves
{
    /// <summary>
    /// Why <paramref name="nodeId"/> may not go under <paramref name="parentId"/> (null: the top
    /// level) before <paramref name="beforeNodeId"/> (null: at the end), in German; null when it may.
    /// </summary>
    public static string? Refusal(Incident incident, Guid nodeId, Guid? parentId, Guid? beforeNodeId)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (incident.FindCommandNode(nodeId) is not { } node)
        {
            return "Abschnitt nicht gefunden.";
        }

        if (!node.IsLive)
        {
            return "Abschnitt ist aufgelöst.";
        }

        if (node.Kind == CommandNodeKind.Partner)
        {
            return "Eine Einsatzleitung lässt sich nicht verschieben.";
        }

        if (parentId is { } pid)
        {
            if (node.Kind == CommandNodeKind.Bereitstellung)
            {
                return "Eine Bereitstellung liegt in keinem Abschnitt.";
            }

            if (pid == nodeId)
            {
                return "Ein Abschnitt liegt nicht in sich selbst.";
            }

            if (incident.FindCommandNode(pid) is not { IsLive: true, Kind: CommandNodeKind.Abschnitt })
            {
                return "Ein Unterabschnitt liegt in einem bestehenden Abschnitt.";
            }

            // Two levels only: an Abschnitt that still leads Unterabschnitte cannot become one.
            if (incident.CommandNodes.Any(n => n.IsLive && n.ParentId == nodeId))
            {
                return "Erst die Unterabschnitte verschieben.";
            }
        }

        if (beforeNodeId is { } bid && bid != nodeId
            && (incident.FindCommandNode(bid) is not { IsLive: true } before || !IsSiblingAt(before, parentId)))
        {
            return "Dort lässt sich nichts einordnen.";
        }

        if (incident.CommandNodes.Any(n =>
                n.IsLive && n.Id != nodeId && n.ParentId == parentId
                && string.Equals(n.Name, node.Name, StringComparison.OrdinalIgnoreCase)))
        {
            return "Name ist bereits vergeben.";
        }

        return null;
    }

    /// <summary>
    /// The nodes that share a place with one under <paramref name="parentId"/>: the Unterabschnitte
    /// of that Abschnitt, or at the top the Bereitstellungen and Abschnitte. Partners have a row of
    /// their own.
    /// </summary>
    public static bool IsSiblingAt(CommandNode node, Guid? parentId)
    {
        ArgumentNullException.ThrowIfNull(node);

        return parentId is null
            ? node.ParentId is null && node.Kind is CommandNodeKind.Abschnitt or CommandNodeKind.Bereitstellung
            : node.ParentId == parentId;
    }
}
