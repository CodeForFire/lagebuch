namespace Feuerwehr.Domain.ValueObjects;

/// <summary>
/// Composes the complete Bavarian Einsatznummer — <c>&lt;Einsatzart&gt; 1.2 &lt;JJMMTT&gt; &lt;lfd.Nr&gt;</c>,
/// e.g. <c>"B 1.2 260715 1297"</c> — from its parts. Everything is free text: the parts are joined
/// around the fixed Leitstelle segment with single spaces, and an entry that carries nothing but the
/// constant (all parts blank) yields <c>null</c>, so a blank prompt produces no number.
/// </summary>
public static class EinsatznummerFormat
{
    public const string Leitstelle = "1.2";

    public static string? Compose(string? art, string? date, string? number)
    {
        var a = art?.Trim() ?? string.Empty;
        var d = date?.Trim() ?? string.Empty;
        var n = number?.Trim() ?? string.Empty;

        if (a.Length == 0 && d.Length == 0 && n.Length == 0)
            return null;

        return string.Join(' ', new[] { a, Leitstelle, d, n }.Where(s => s.Length > 0));
    }
}
