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
