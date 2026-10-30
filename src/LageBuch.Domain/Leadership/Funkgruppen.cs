namespace LageBuch.Domain.Leadership;

/// <summary>
/// The TMO and DMO Funkgruppen an Einsatzleitung works on (#828): up to <see cref="MaxPerKind"/> of
/// each, in the order they were entered. Each entry is the group without its kind, e.g.
/// "FW_MUSTER 1000", because the kind is what the list is. <see cref="Normalized"/> is the one place
/// entries are trimmed, deduplicated and capped.
/// </summary>
public sealed record Funkgruppen
{
    /// <summary>How many Funkgruppen of one kind an Einsatzleitung holds.</summary>
    public const int MaxPerKind = 3;

    public Funkgruppen(IReadOnlyList<string>? tmo, IReadOnlyList<string>? dmo)
    {
        Tmo = tmo ?? [];
        Dmo = dmo ?? [];
    }

    public static Funkgruppen Empty { get; } = new(null, null);

    /// <summary>The TMO (Netzbetrieb) Funkgruppen.</summary>
    public IReadOnlyList<string> Tmo { get; }

    /// <summary>The DMO (Direktbetrieb) Funkgruppen.</summary>
    public IReadOnlyList<string> Dmo { get; }

    public bool IsEmpty => Tmo.Count == 0 && Dmo.Count == 0;

    /// <summary>
    /// The Funkgruppen as read aloud and printed: "TMO A, B · DMO C", each kind only when it has
    /// one; empty when there are none.
    /// </summary>
    public string Text => string.Join(
        " · ",
        new[] { Part("TMO", Tmo), Part("DMO", Dmo) }.Where(p => p.Length > 0));

    /// <summary>
    /// Returns a copy with every entry trimmed, blanks and repeats (ordinal) dropped, and throws when
    /// an entry is over the cap or a kind holds more than <see cref="MaxPerKind"/>.
    /// </summary>
    public Funkgruppen Normalized() => new(Clean(Tmo, "TMO"), Clean(Dmo, "DMO"));

    /// <summary>
    /// What a file or a sync host recorded, read without throwing: entries <see cref="Normalized"/>
    /// would refuse are skipped and each kind stops at <see cref="MaxPerKind"/>. A command is not read
    /// this way: a peer asking for a fourth Funkgruppe is refused, not trimmed.
    /// </summary>
    public static Funkgruppen FromRecord(IEnumerable<string?>? tmo, IEnumerable<string?>? dmo) =>
        new(Salvage(tmo), Salvage(dmo));

    public bool Equals(Funkgruppen? other) =>
        other is not null && Tmo.SequenceEqual(other.Tmo, StringComparer.Ordinal)
        && Dmo.SequenceEqual(other.Dmo, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var t in Tmo)
        {
            hash.Add(t, StringComparer.Ordinal);
        }

        hash.Add('|');
        foreach (var d in Dmo)
        {
            hash.Add(d, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    private static string Part(string kind, IReadOnlyList<string> groups) =>
        groups.Count == 0 ? string.Empty : $"{kind} {string.Join(", ", groups)}";

    private static string[] Salvage(IEnumerable<string?>? groups) =>
        [.. (groups ?? [])
            .Select(g => g?.Trim() ?? string.Empty)
            .Where(g => g.Length is > 0 and <= CommandNode.MaxTextLength)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxPerKind)];

    private static string[] Clean(IReadOnlyList<string> groups, string kind)
    {
        var cleaned = groups
            .Select(g => CommandNode.Clean(g, CommandNode.MaxTextLength, $"Funkgruppe {kind}"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (cleaned.Length > MaxPerKind)
        {
            throw new ArgumentException($"Höchstens {MaxPerKind} Funkgruppen {kind}.", nameof(groups));
        }

        return cleaned;
    }
}
