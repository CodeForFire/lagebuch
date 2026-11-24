namespace LageBuch.Domain.Etb;

/// <summary>
/// Rules shared by every consumer of <see cref="EtbDirection"/>, so the "which directions are
/// machine-written?" question is answered in one place instead of drifting between the domain
/// and the view model.
/// </summary>
public static class EtbDirections
{
    // Before #972 a Kartenebene's import and removal were written as EtbDirection.Measurement.
    // They are bookkeeping, and new files write them as System; an old file keeps ordinal 4 on
    // disk, so this prefix is how its lines are still read as System.
    private const string LegacyMapOverlayPrefix = "Kartenebene ";

    /// <summary>
    /// Written by the app, never chosen by a human: omitted from the direction picker and never
    /// editable after the fact. Deliberately *not* the same question as "may it be hidden" --
    /// <see cref="EtbDirection.Measurement"/> is app-written too, but has its own filter (#972).
    /// </summary>
    public static bool IsAppWritten(EtbDirection direction) =>
        direction is EtbDirection.System or EtbDirection.Measurement;

    /// <summary>
    /// A measured value (#424, #972): a Gasmessung, hidden by "Messwerte ausblenden" and printed in
    /// the Bericht's Messungen section rather than in its ETB. A Kartenebene line an older build
    /// wrote with the Measurement direction is not one; see <see cref="IsSystem"/>.
    /// </summary>
    public static bool IsMeasurement(EtbDirection direction, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return direction == EtbDirection.Measurement && !IsLegacyMapOverlayLine(text);
    }

    /// <summary>
    /// Bookkeeping, hidden by "Systemmeldungen ausblenden": every System entry, and the Kartenebene
    /// lines an older build wrote as Measurement.
    /// </summary>
    public static bool IsSystem(EtbDirection direction, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return direction == EtbDirection.System
            || (direction == EtbDirection.Measurement && IsLegacyMapOverlayLine(text));
    }

    private static bool IsLegacyMapOverlayLine(string text) =>
        text.StartsWith(LegacyMapOverlayPrefix, StringComparison.Ordinal);
}
