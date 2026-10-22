namespace LageBuch.Domain.Leadership;

/// <summary>
/// Repairs command nodes read from an untrusted source (an <c>.fwincident</c> file or a sync
/// snapshot) so one implementation guards both read paths. Nothing here throws: a node that
/// breaks the tree's rules degrades to the nearest valid shape rather than failing the load.
/// </summary>
internal static class CommandNodeRepair
{
    /// <summary>
    /// An undefined kind reads as an Abschnitt. Only an Unterabschnitt keeps a parent, and only when
    /// it names another Abschnitt in the list; any other Unterabschnitt (missing parent, itself, a
    /// non-Abschnitt) reads as a top-level Abschnitt.
    /// </summary>
    internal static List<CommandNode> Resolve(IEnumerable<CommandNode> nodes)
    {
        var list = nodes.Select(n => Enum.IsDefined(n.Kind) ? n : Rebuild(n, CommandNodeKind.Abschnitt, n.ParentId)).ToList();
        var abschnittIds = list.Where(n => n.Kind == CommandNodeKind.Abschnitt).Select(n => n.Id).ToHashSet();

        return list.ConvertAll(n =>
        {
            if (n.Kind != CommandNodeKind.Unterabschnitt)
            {
                return n.ParentId is null ? n : Rebuild(n, n.Kind, null);
            }

            return n.ParentId is { } parent && parent != n.Id && abschnittIds.Contains(parent)
                ? n
                : Rebuild(n, CommandNodeKind.Abschnitt, null);
        });
    }

    private static CommandNode Rebuild(CommandNode n, CommandNodeKind kind, Guid? parentId) =>
        CommandNode.Rehydrate(
            n.Id, kind, n.Name, parentId, n.Auftrag, n.Standort, n.Funkgruppe, n.Rufname, n.Telefon, n.DissolvedAt);
}
