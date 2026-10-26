namespace LageBuch.Domain.Leadership;

/// <summary>One row of the derived Führungsorganisation.</summary>
/// <param name="Depth">0 is top level, 1 lies under the Einsatzleitung, 2 is an Unterabschnitt.</param>
/// <param name="Kind">What the row is.</param>
/// <param name="Node">The command node; null for the Einsatzleitung and the Fachberater row.</param>
/// <param name="Leiter">The running Leiter.</param>
/// <param name="Melder">The running Melder.</param>
/// <param name="Others">
/// Führungsassistenten (Einsatzleitung); everyone else in a node apart from its Leiter, its Melder
/// and the Fachberater (unslotted members, and a second Leiter or Melder or a Führungsassistent a
/// peer or an old file put there); or the Fachberater.
/// </param>
/// <param name="Units">The units placed directly in the node.</param>
public sealed record ReportingEntry(
    int Depth,
    ReportingEntryKind Kind,
    CommandNode? Node,
    RoleAssignment? Leiter,
    RoleAssignment? Melder,
    IReadOnlyList<RoleAssignment> Others,
    IReadOnlyList<ForceUnit> Units)
{
    /// <summary>
    /// The Einsatzleitung only: running Leiter and Melder with no node and no Abschnitt beyond the
    /// first of each (an EAL typed on FUNKTIONEN without an Abschnitt), in the order they were
    /// assigned. Kept apart from <see cref="Others"/>, which there means the Führungsassistenten.
    /// Empty for every other row.
    /// </summary>
    public IReadOnlyList<RoleAssignment> Further { get; init; } = [];
}
