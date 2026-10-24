using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// What the Führung board and its person dialog call a node's Leiter: the short form said on a
/// command post. A Bereitstellungsraum has none, so it stays "Leiter". The Bericht writes "Leiter"
/// throughout and does not use this.
/// </summary>
internal static class LeiterLabels
{
    /// <summary>The short form for a node of <paramref name="kind"/>; null is the Einsatzleitung.</summary>
    public static string Of(CommandNodeKind? kind) => kind switch
    {
        null or CommandNodeKind.Partner => "EL",
        CommandNodeKind.Abschnitt => "EAL",
        CommandNodeKind.Unterabschnitt => "UAL",
        _ => "Leiter",
    };
}
