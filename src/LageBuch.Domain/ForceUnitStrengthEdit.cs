namespace LageBuch.Domain;

/// <summary>
/// One prior Stärke of a force unit, retained when it was corrected (#76) — the force-row sibling
/// of <see cref="Etb.EtbEntryEdit"/>: what the counts were before, who corrected them, when.
/// </summary>
public sealed record ForceUnitStrengthEdit(
    int PreviousOfficerCount,
    int PreviousPersonnelCount,
    int PreviousScbaCount,
    string EditedBy,
    DateTimeOffset EditedAt);
