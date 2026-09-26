using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Helpers for the Stammdaten catalogues (<c>UnitStatus</c>, <c>Roles</c>) that describe values
/// recorded in an incident. Those values live in the incident file, but the catalogue they came
/// from is global and edited freely afterwards, so an incident opened later can hold a value the
/// catalogue no longer lists — from an older Einsatz, an imported file, or a joined client running
/// different Stammdaten. See issue #337 for whether an incident should carry its own snapshot.
/// <para>
/// Nothing here rejects a value. An Einsatz in progress must never be blocked by a missing
/// Stammdaten row, so an unknown value is accepted, shown and saved as typed.
/// </para>
/// </summary>
public static class StammdatenCatalogue
{
    /// <summary>
    /// Trims <paramref name="value"/> and adopts the catalogue's own spelling when it matches an
    /// entry apart from case or surrounding whitespace, so "el" and "EL " both record as the
    /// catalogue's "EL" rather than as three separate Funktionen. An unknown value is returned
    /// trimmed but otherwise untouched.
    /// </summary>
    public static string? Normalize(string? value, IReadOnlyList<string> catalogue)
    {
        var trimmed = value?.Trim();
        return Match(trimmed, catalogue) ?? trimmed;
    }

    /// <summary>
    /// Whether a non-blank <paramref name="value"/> is absent from the catalogue — the cue for a
    /// non-blocking hint. Always false for an empty catalogue: a fresh install has no Stammdaten at
    /// all, and flagging every entry there would be noise, not information.
    /// </summary>
    public static bool IsUnknown(string? value, IReadOnlyList<string> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return catalogue.Count > 0
               && !string.IsNullOrWhiteSpace(value)
               && Match(value.Trim(), catalogue) is null;
    }

    /// <summary>
    /// The catalogue, plus <paramref name="current"/> when the list does not already hold that exact
    /// string — so a closed picker bound to this list can still display a value the catalogue has
    /// since lost. Without it the control has nothing to select and renders blank, hiding a status
    /// that is recorded in the incident (see the Status columns in <c>ForcesView.axaml</c>).
    /// <para>
    /// The comparison is ordinal and exact, unlike <see cref="Normalize"/>'s: a picker matches its
    /// selection against the list by equality, so a value differing only in case still needs its own
    /// entry or it would render blank just the same.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Including(IReadOnlyList<string> catalogue, string? current)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (string.IsNullOrWhiteSpace(current) || catalogue.Contains(current, StringComparer.Ordinal))
        {
            return catalogue;
        }

        return [.. catalogue, current];
    }

    /// <summary>
    /// The Trupp-Typ <paramref name="designation"/> names, or null when the catalogue has no such
    /// row. Matched the same way as every other catalogue value here -- trimmed, ignoring case --
    /// so "csa-trupp " still finds the row the Stammdaten spell "CSA-Trupp".
    /// <para>
    /// This is how the Atemschutz form learns a Trupp's crew size and Einsatzzeit since #398 moved
    /// them off the compiled-in names. A null is not an error: an unlisted designation is simply a
    /// Trupp the Stammdaten do not describe, and the caller falls back to an ordinary two-person
    /// Trupp rather than refusing to register it mid-Einsatz.
    /// </para>
    /// </summary>
    public static TruppType? Find(string? designation, IReadOnlyList<TruppType> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return FindByName(designation, catalogue, entry => entry?.Name);
    }

    /// <summary>
    /// The Stammdaten row for the Funktion <paramref name="name"/>, or null when the catalogue has
    /// no such row. Matched the same way as every other lookup here -- trimmed, ignoring case -- so
    /// "el" finds the row the Stammdaten spell "EL".
    /// <para>
    /// It returns the whole row rather than the name because #470 put the Funktion's uniqueness on
    /// it, and the caller needs the mode to know whether a second holder may be refused. A null is
    /// not an error: an unlisted Funktion is one the Stammdaten do not describe, it carries no mode,
    /// so nothing is refused for it -- the tolerance this whole class exists for.
    /// </para>
    /// </summary>
    public static Role? Find(string? name, IReadOnlyList<Role> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        return FindByName(name, catalogue, entry => entry?.Name);
    }

    // One loop for both row lookups, so the trimming, the case-insensitive comparison and the
    // first-match rule cannot drift apart between them; only the row type differs. A null row or a
    // null name is tolerated as it always was, which costs nothing next to a mid-Einsatz crash.
    private static T? FindByName<T>(string? name, IReadOnlyList<T> catalogue, Func<T, string?> nameOf)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return default;
        }

        foreach (var entry in catalogue)
        {
            if (string.Equals(nameOf(entry)?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return default;
    }

    /// <summary>The catalogue's own spelling of <paramref name="trimmed"/>, or null if it has none.</summary>
    private static string? Match(string? trimmed, IReadOnlyList<string> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        foreach (var entry in catalogue)
        {
            if (string.Equals(entry?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }
}
