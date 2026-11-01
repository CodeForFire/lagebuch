namespace LageBuch.Domain.Leadership;

/// <summary>
/// Where and how a Führungsstelle can be reached: its Standort, Rufname, Telefon, its TMO and DMO
/// Funkgruppen and the Führungsfahrzeug it is led from (#829), as its Funkrufname.
/// <see cref="Normalized"/> is the one place its texts are trimmed and capped.
/// </summary>
public sealed record LeadershipPost(
    string? Standort, string? Rufname, string? Telefon, Funkgruppen Funkgruppen, string? Fuehrungsfahrzeug = null)
{
    public static LeadershipPost Empty { get; } = new(null, null, null, Funkgruppen.Empty);

    /// <summary>Returns a copy with every text trimmed and blank mapped to null; throws on a cap breach.</summary>
    public LeadershipPost Normalized() => new(
        CommandNode.Clean(Standort, CommandNode.MaxTextLength, "Standort"),
        CommandNode.Clean(Rufname, CommandNode.MaxTextLength, "Rufname"),
        CommandNode.Clean(Telefon, CommandNode.MaxPhoneLength, "Telefon"),
        Funkgruppen.Normalized(),
        CommandNode.Clean(Fuehrungsfahrzeug, CommandNode.MaxTextLength, "Führungsfahrzeug"));
}
