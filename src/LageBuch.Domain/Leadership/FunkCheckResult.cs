namespace LageBuch.Domain.Leadership;

/// <summary>A node's <see cref="FunkCheck"/> against the level above it.</summary>
/// <param name="NodeId">The node checked.</param>
/// <param name="ParentLabel">The level above, as a hint names it: "Einsatzleitung" or "Abschnitt Nord".</param>
/// <param name="Tmo">The TMO side.</param>
/// <param name="Dmo">The DMO side.</param>
public sealed record FunkCheckResult(Guid NodeId, string ParentLabel, FunkKindCheck Tmo, FunkKindCheck Dmo);
