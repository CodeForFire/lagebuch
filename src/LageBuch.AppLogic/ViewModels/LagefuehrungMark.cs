namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// What an Einsatzleitung card on the Führung board shows of the Lageführung (#926): the LAGEFÜHRUNG
/// badge on the card that holds it, the LAGEFÜHRUNG button on every other. While the board has no
/// partner Einsatzleitung there is nobody to tell apart, so no card shows either.
/// </summary>
public sealed record LagefuehrungMark(bool ShowsBadge, bool ShowsTake)
{
    public static LagefuehrungMark None { get; } = new(false, false);

    /// <summary>The mark for a card that <paramref name="holds"/> the Lageführung or not.</summary>
    public static LagefuehrungMark For(bool holds, bool hasPartners, bool isReadOnly) =>
        hasPartners ? new(holds, !holds && !isReadOnly) : None;
}
