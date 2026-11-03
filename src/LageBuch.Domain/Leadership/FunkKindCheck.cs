namespace LageBuch.Domain.Leadership;

/// <summary>One kind of a <see cref="FunkCheckResult"/>.</summary>
/// <param name="IsChecked">
/// False while either side has no Funkgruppe of this kind: not entered yet is not a mismatch.
/// </param>
/// <param name="Shared">The node's own Funkgruppen of this kind the level above has too, as the node writes them.</param>
public sealed record FunkKindCheck(bool IsChecked, IReadOnlyList<string> Shared)
{
    public static FunkKindCheck NotChecked { get; } = new(false, []);

    /// <summary>Both sides have Funkgruppen of this kind and none in common.</summary>
    public bool IsMismatch => IsChecked && Shared.Count == 0;

    /// <summary>Whether the node's <paramref name="group"/> is shared; null when the kind is not checked.</summary>
    public bool? IsShared(string group) => IsChecked ? Shared.Contains(group, StringComparer.Ordinal) : null;
}
