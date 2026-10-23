using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// What the Personal roster knows about a person just named in a form, and the rule for writing it
/// into the form's Funkrufname and Handynummer (#656): a field is the roster's to write while it is
/// blank or still holds what the roster last put there, so correcting the name swaps the previous
/// person's number for the new one's, or clears it when the name matches nobody. A value typed by
/// hand outranks the roster, which may be out of date, and is left alone.
/// </summary>
/// <remarks>
/// Shared by the Funktionen tab and the Führung board's person dialog. Each form keeps what the
/// roster last wrote per field itself and passes it back in as <c>prefilled</c>.
/// </remarks>
public static class RosterLookup
{
    /// <summary>The roster entry whose display name is <paramref name="personName"/>, ignoring case; null for none.</summary>
    public static Person? Find(IReadOnlyList<Person> personnel, string? personName)
    {
        ArgumentNullException.ThrowIfNull(personnel);
        return personnel.FirstOrDefault(
            p => string.Equals(p.DisplayName, personName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Writes <paramref name="fromRoster"/> through <paramref name="set"/> unless the field holds
    /// something typed by hand, and returns what the roster now owns in that field (null when the
    /// field is the Lagebuchführer's).
    /// </summary>
    /// <param name="current">What the field holds now.</param>
    /// <param name="prefilled">What the roster last wrote into it.</param>
    /// <param name="fromRoster">What the roster has for the person now named; null for nobody.</param>
    /// <param name="set">Writes the field.</param>
    public static string? Refill(string? current, string? prefilled, string? fromRoster, Action<string?> set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var rosterOwnsIt = string.IsNullOrWhiteSpace(current)
            || string.Equals(current, prefilled, StringComparison.Ordinal);
        if (!rosterOwnsIt)
        {
            return null;
        }

        if (!string.Equals(current, fromRoster, StringComparison.Ordinal)
            && !(string.IsNullOrWhiteSpace(current) && string.IsNullOrWhiteSpace(fromRoster)))
        {
            set(fromRoster);
        }

        return fromRoster;
    }
}
