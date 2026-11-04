using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One Funkgruppe as a Führung board card lists it, marked against the level above (#878).
/// </summary>
/// <param name="Text">The Funkgruppe as the node records it.</param>
/// <param name="IsShared">
/// Whether the level above has it too; null when the kind is not checked, because one side has no
/// Funkgruppe of it yet, and the line then carries no mark.
/// </param>
public sealed record FunkgruppeLine(string Text, bool? IsShared)
{
    /// <summary>The level above has it: a check mark.</summary>
    public bool IsMarkedShared => IsShared == true;

    /// <summary>The kind is checked and the level above lacks it: the warning mark.</summary>
    public bool IsMarkedNotShared => IsShared == false;

    /// <summary>What a screen reader says for the mark; empty when there is none.</summary>
    public string MarkName =>
        IsMarkedShared ? "gemeinsame Funkgruppe"
        : IsMarkedNotShared ? "keine gemeinsame Funkgruppe"
        : string.Empty;

    /// <summary>
    /// Lit while the pointer rests on the same Funkgruppe on another card. Not part of the line's
    /// value: a refresh that brings the same lines keeps the shown ones, highlight and all (#294).
    /// </summary>
    public HighlightMark Highlight { get; } = new();

    public bool Equals(FunkgruppeLine? other) =>
        other is not null && string.Equals(Text, other.Text, StringComparison.Ordinal) && IsShared == other.IsShared;

    public override int GetHashCode() => HashCode.Combine(Text, IsShared);

    /// <summary>The new lines when they differ from <paramref name="shown"/>, else null, so an unchanged list is not rebuilt.</summary>
    internal static FunkgruppeLine[]? Changed(IReadOnlyList<FunkgruppeLine> shown, FunkgruppeLine[] lines) =>
        shown.SequenceEqual(lines) ? null : lines;

    /// <summary>The lines for <paramref name="groups"/>, each marked by <paramref name="check"/> (unmarked when null).</summary>
    internal static FunkgruppeLine[] Of(IReadOnlyList<string> groups, FunkKindCheck? check) =>
        [.. groups.Select(g => new FunkgruppeLine(g, check?.IsShared(g)))];
}
