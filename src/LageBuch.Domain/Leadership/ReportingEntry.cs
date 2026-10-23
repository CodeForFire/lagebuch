namespace LageBuch.Domain.Leadership;

/// <summary>One row of the derived Führungsorganisation.</summary>
/// <param name="Depth">0 is top level, 1 lies under the Einsatzleitung, 2 is an Unterabschnitt.</param>
/// <param name="Kind">What the row is.</param>
/// <param name="Node">The command node; null for the Einsatzleitung and the Fachberater row.</param>
/// <param name="Leiter">The running Leiter.</param>
/// <param name="Melder">The running Melder.</param>
/// <param name="Others">Führungsassistenten (Einsatzleitung), unslotted members (a node) or the Fachberater.</param>
/// <param name="Units">The units placed directly in the node.</param>
public sealed record ReportingEntry(
    int Depth,
    ReportingEntryKind Kind,
    CommandNode? Node,
    RoleAssignment? Leiter,
    RoleAssignment? Melder,
    IReadOnlyList<RoleAssignment> Others,
    IReadOnlyList<ForceUnit> Units);
