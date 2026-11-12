namespace LageBuch.Domain.Leadership;

/// <summary>
/// The Funkgruppen cross-check of the Führungsorganisation (#878): whether each live node shares a
/// TMO and a DMO Funkgruppe with the level above it. An Abschnitt, a Bereitstellung and a partner
/// Einsatzleitung answer to our Einsatzleitung; an Unterabschnitt to its Abschnitt, or to our
/// Einsatzleitung once that Abschnitt is gone. Our Einsatzleitung is in turn checked against its
/// partners (<see cref="OfEinsatzleitung"/>). Derived on demand, never stored.
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

    /// <summary>
    /// Our Einsatzleitung's own Funkgruppen against its live partner Einsatzleitungen: a group is
    /// shared when any partner has it, and each kind names the partners it shares none with.
    /// Abschnitte and Bereitstellungen are not asked: they answer to the Einsatzleitung, not it to them.
    /// </summary>
    public static EinsatzleitungFunkCheck OfEinsatzleitung(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var post = incident.LeadershipPost.Funkgruppen;
        var partners = incident.CommandNodes.Where(n => n.IsLive && n.Kind == CommandNodeKind.Partner).ToList();
        var (tmo, tmoMismatch) = Against(post.Tmo, partners.Select(p => (p.Name, p.Funkgruppen.Tmo)));
        var (dmo, dmoMismatch) = Against(post.Dmo, partners.Select(p => (p.Name, p.Funkgruppen.Dmo)));
        return new EinsatzleitungFunkCheck(tmo, dmo, tmoMismatch, dmoMismatch);
    }

    private static (FunkKindCheck Check, IReadOnlyList<string> MismatchWith) Against(
        IReadOnlyList<string> own, IEnumerable<(string Name, IReadOnlyList<string> Groups)> partners)
    {
        var checks = partners.Select(p => (p.Name, Check: Compare(own, p.Groups))).Where(c => c.Check.IsChecked).ToList();
        if (checks.Count == 0)
        {
            return (FunkKindCheck.NotChecked, []);
        }

        var shared = own.Where(g => checks.Exists(c => c.Check.Shared.Contains(g, StringComparer.Ordinal)));
        return (new FunkKindCheck(true, [.. shared]), [.. checks.Where(c => c.Check.IsMismatch).Select(c => c.Name)]);
    }

    // Both sides empty: nothing entered yet, so nothing to warn about. One side empty: that side
    // cannot reach the other on this kind, which is a mismatch like two different groups.
    private static FunkKindCheck Compare(IReadOnlyList<string> own, IReadOnlyList<string> above) =>
        own.Count == 0 && above.Count == 0
            ? FunkKindCheck.NotChecked
            : new FunkKindCheck(true, [.. own.Where(g => above.Any(a => SameGroup(g, a)))]);
}
