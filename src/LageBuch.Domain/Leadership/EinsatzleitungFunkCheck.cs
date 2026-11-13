namespace LageBuch.Domain.Leadership;

/// <summary>Our Einsatzleitung's <see cref="FunkCheck.OfEinsatzleitung"/> against its live partners.</summary>
/// <param name="Tmo">The TMO side: a group is shared when any partner has it; not checked without a partner to check against.</param>
/// <param name="Dmo">The DMO side, as <paramref name="Tmo"/>.</param>
/// <param name="TmoMismatchWith">The names of the partners sharing no TMO Funkgruppe with us, in node order.</param>
/// <param name="DmoMismatchWith">The names of the partners sharing no DMO Funkgruppe with us, in node order.</param>
public sealed record EinsatzleitungFunkCheck(
    FunkKindCheck Tmo, FunkKindCheck Dmo, IReadOnlyList<string> TmoMismatchWith, IReadOnlyList<string> DmoMismatchWith);
