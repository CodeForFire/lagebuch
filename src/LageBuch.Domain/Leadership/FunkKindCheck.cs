namespace LageBuch.Domain.Leadership;

/// <summary>One kind of a <see cref="FunkCheckResult"/>.</summary>
/// <param name="IsChecked">
/// False while neither side has a Funkgruppe of this kind: nothing entered yet is not a mismatch.
/// One side without any is checked, and a mismatch: it cannot reach the other on this kind.
/// </param>
/// <param name="Shared">The node's own Funkgruppen of this kind the level above has too, as the node writes them.</param>
public sealed record FunkKindCheck(bool IsChecked, IReadOnlyList<string> Shared)
{
    public static FunkKindCheck NotChecked { get; } = new(false, []);

    /// <summary>The kind is checked and the two sides have none of it in common.</summary>
    public bool IsMismatch => IsChecked && Shared.Count == 0;

    /// <summary>
    /// Whether the node's <paramref name="group"/> is shared: true when the level above has it, false
    /// when this kind is a <see cref="IsMismatch"/>. Null when the kind is not checked, and for a
    /// further group of a kind that already shares another one: one in common is enough to reach the
    /// level above, so that group is no warning (#887).
    /// </summary>
    public bool? IsShared(string group) =>
        !IsChecked ? null
        : Shared.Contains(group, StringComparer.Ordinal) ? true
        : IsMismatch ? false
        : null;
}
