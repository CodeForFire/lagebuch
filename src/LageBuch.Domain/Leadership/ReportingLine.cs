namespace LageBuch.Domain.Leadership;

/// <summary>
/// The Führungsorganisation derived from the incident's nodes, Funktionen and units: one tree, no
/// second source of truth. Only running assignments and live nodes take part.
/// </summary>
public static class ReportingLine
{
    public static IReadOnlyList<ReportingEntry> Of(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var running = incident.Roles.Where(r => r.To is null).ToList();
        var live = incident.CommandNodes.Where(n => n.IsLive).ToList();
        var entries = new List<ReportingEntry> { EinsatzleitungEntry(running) };

        foreach (var node in live.Where(n => n.Kind is CommandNodeKind.Bereitstellung or CommandNodeKind.Abschnitt))
        {
            entries.Add(NodeEntry(1, node, running, incident));
            if (node.Kind == CommandNodeKind.Abschnitt)
            {
                foreach (var sub in live.Where(n => n.Kind == CommandNodeKind.Unterabschnitt && n.ParentId == node.Id))
                {
                    entries.Add(NodeEntry(2, sub, running, incident));
                }
            }
        }

        // A live Unterabschnitt under a dissolved Abschnitt (the repair keeps it) would otherwise show
        // nowhere, taking its units and Funktionen with it; it stands on its own one level down.
        foreach (var orphan in live.Where(n => n.Kind == CommandNodeKind.Unterabschnitt
            && !live.Exists(p => p.Kind == CommandNodeKind.Abschnitt && p.Id == n.ParentId)))
        {
            entries.Add(NodeEntry(1, orphan, running, incident));
        }

        foreach (var partner in live.Where(n => n.Kind == CommandNodeKind.Partner))
        {
            entries.Add(NodeEntry(0, partner, running, incident));
        }

        var fachberater = running.Where(r => r.Slot == RoleSlot.Fachberater).ToList();
        if (fachberater.Count > 0)
        {
            entries.Add(new ReportingEntry(0, ReportingEntryKind.Fachberater, null, null, null, fachberater, []));
        }

        return entries;
    }

    /// <summary>Units without a place: no node, or one that is missing, dissolved or a Partner.</summary>
    public static IReadOnlyList<ForceUnit> UnplacedUnits(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return incident.Forces
            .Where(u => u.NodeId is not { } id
                || incident.FindCommandNode(id) is not { IsLive: true, Kind: not CommandNodeKind.Partner })
            .ToList();
    }

    /// <summary>Running assignments with a free-text Section and no node, grouped by that Section.</summary>
    public static IReadOnlyList<(string Section, IReadOnlyList<RoleAssignment> Assignments)> FreeTextSections(
        Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var groups = new List<(string Section, List<RoleAssignment> Assignments)>();
        foreach (var role in incident.Roles.Where(r => r.To is null && r.NodeId is null))
        {
            if (role.Section?.Trim() is not { Length: > 0 } section)
            {
                continue;
            }

            var index = groups.FindIndex(g => string.Equals(g.Section, section, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                groups.Add((section, [role]));
            }
            else
            {
                groups[index].Assignments.Add(role);
            }
        }

        return groups.Select(g => (g.Section, (IReadOnlyList<RoleAssignment>)g.Assignments)).ToList();
    }

    // The Einsatzleitung is everyone with no node and no free-text Abschnitt. FUNKTIONEN stamps the
    // Stammdaten slot on whatever is typed there, so a Melder or a second Leiter (an EAL beside the
    // EL) arrives here too: the first of each holds the post, the rest stand in Further, and nobody
    // a Bericht counts is left out.
    private static ReportingEntry EinsatzleitungEntry(List<RoleAssignment> running)
    {
        var atRoot = running.Where(r => r.NodeId is null && string.IsNullOrWhiteSpace(r.Section)).ToList();
        var leiter = atRoot.Find(r => r.Slot == RoleSlot.Leiter);
        var melder = atRoot.Find(r => r.Slot == RoleSlot.Melder);
        return new ReportingEntry(
            0,
            ReportingEntryKind.Einsatzleitung,
            null,
            leiter,
            melder,
            running.Where(r => r.Slot == RoleSlot.Fuehrungsassistent && r.NodeId is null).ToList(),
            [])
        {
            Further = atRoot
                .Where(r => r.Slot is RoleSlot.Leiter or RoleSlot.Melder
                    && !ReferenceEquals(r, leiter) && !ReferenceEquals(r, melder))
                .ToList(),
        };
    }

    private static ReportingEntry NodeEntry(int depth, CommandNode node, List<RoleAssignment> running, Incident incident)
    {
        var inNode = running.Where(r => r.NodeId == node.Id).ToList();
        var leiter = inNode.Find(r => r.Slot == RoleSlot.Leiter);
        var melder = inNode.Find(r => r.Slot == RoleSlot.Melder);
        var kind = node.Kind switch
        {
            CommandNodeKind.Bereitstellung => ReportingEntryKind.Bereitstellung,
            CommandNodeKind.Unterabschnitt => ReportingEntryKind.Unterabschnitt,
            CommandNodeKind.Partner => ReportingEntryKind.Partner,
            _ => ReportingEntryKind.Abschnitt,
        };

        // The Fachberater have their own row; everyone else not in the post is shown as a member,
        // a second Leiter or Melder included, so a node that cannot be dissolved shows why.
        var others = inNode
            .Where(r => r.Slot != RoleSlot.Fachberater && !ReferenceEquals(r, leiter) && !ReferenceEquals(r, melder))
            .ToList();
        return new ReportingEntry(
            depth,
            kind,
            node,
            leiter,
            melder,
            others,
            node.Kind == CommandNodeKind.Partner ? [] : incident.Forces.Where(u => u.NodeId == node.Id).ToList());
    }
}
