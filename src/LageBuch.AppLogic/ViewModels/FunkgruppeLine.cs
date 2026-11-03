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
    public string MarkName => IsShared switch
    {
        true => "gemeinsame Funkgruppe",
        false => "keine gemeinsame Funkgruppe",
        null => string.Empty,
    };

    /// <summary>The lines for <paramref name="groups"/>, each marked by <paramref name="check"/> (unmarked when null).</summary>
    internal static FunkgruppeLine[] Of(IReadOnlyList<string> groups, FunkKindCheck? check) =>
        [.. groups.Select(g => new FunkgruppeLine(g, check?.IsShared(g)))];
}
