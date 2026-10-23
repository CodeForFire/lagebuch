namespace LageBuch.Domain.Leadership;

/// <summary>
/// Where and how a Führungsstelle can be reached: its Standort, Rufname, Telefon and Funkgruppe.
/// <see cref="Normalized"/> is the one place its texts are trimmed and capped.
/// </summary>
public sealed record LeadershipPost(string? Standort, string? Rufname, string? Telefon, string? Funkgruppe)
{
    public static LeadershipPost Empty { get; } = new(null, null, null, null);

    /// <summary>Returns a copy with every text trimmed and blank mapped to null; throws on a cap breach.</summary>
    public LeadershipPost Normalized() => new(
        CommandNode.Clean(Standort, CommandNode.MaxTextLength, "Standort"),
        CommandNode.Clean(Rufname, CommandNode.MaxTextLength, "Rufname"),
        CommandNode.Clean(Telefon, CommandNode.MaxPhoneLength, "Telefon"),
        CommandNode.Clean(Funkgruppe, CommandNode.MaxTextLength, "Funkgruppe"));
}
