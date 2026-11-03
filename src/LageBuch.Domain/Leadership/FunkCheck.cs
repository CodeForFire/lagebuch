namespace LageBuch.Domain.Leadership;

/// <summary>
/// The Funkgruppen cross-check of the Führungsorganisation (#878): whether each live node shares a
/// TMO and a DMO Funkgruppe with the level above it. An Abschnitt, a Bereitstellung and a partner
/// Einsatzleitung answer to our Einsatzleitung; an Unterabschnitt to its Abschnitt, or to our
/// Einsatzleitung once that Abschnitt is gone. Derived on demand, never stored.
/// </summary>
public static class FunkCheck
{
    /// <summary>What the level above is called when it is our own Einsatzleitung.</summary>
    public const string EinsatzleitungLabel = "Einsatzleitung";

    /// <summary>One result per live node, in the incident's node order.</summary>
    public static IReadOnlyList<FunkCheckResult> Of(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var live = incident.CommandNodes.Where(n => n.IsLive).ToList();
        var post = incident.LeadershipPost.Funkgruppen;
        var results = new List<FunkCheckResult>(live.Count);
        foreach (var node in live)
        {
            var abschnitt = node.Kind == CommandNodeKind.Unterabschnitt
                ? live.Find(p => p.Kind == CommandNodeKind.Abschnitt && p.Id == node.ParentId)
                : null;
            var (label, above) = abschnitt is null
                ? (EinsatzleitungLabel, post)
                : ($"{CommandNodeKindLabels.Of(abschnitt.Kind)} {abschnitt.Name}", abschnitt.Funkgruppen);
            results.Add(new FunkCheckResult(
                node.Id,
                label,
                Compare(node.Funkgruppen.Tmo, above.Tmo),
                Compare(node.Funkgruppen.Dmo, above.Dmo)));
        }

        return results;
    }

    /// <summary>
    /// Whether two Funkgruppen name the same group: equal once trimmed, with every run of whitespace
    /// read as one space and case ignored. They are typed as well as picked, so "fw_muster  1000"
    /// is the group the Stammdaten call "FW_MUSTER 1000".
    /// </summary>
    public static bool SameGroup(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return string.Equals(Key(a), Key(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string Key(string group) =>
        string.Join(' ', group.Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries));

    private static FunkKindCheck Compare(IReadOnlyList<string> own, IReadOnlyList<string> above) =>
        own.Count == 0 || above.Count == 0
            ? FunkKindCheck.NotChecked
            : new FunkKindCheck(true, [.. own.Where(g => above.Any(a => SameGroup(g, a)))]);
}
