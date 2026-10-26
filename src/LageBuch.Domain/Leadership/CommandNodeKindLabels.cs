namespace LageBuch.Domain.Leadership;

/// <summary>What a <see cref="CommandNodeKind"/> is called on the board and in the Bericht.</summary>
public static class CommandNodeKindLabels
{
    /// <summary>The German label; a partner's node is an Einsatzleitung.</summary>
    public static string Of(CommandNodeKind kind) => kind switch
    {
        CommandNodeKind.Unterabschnitt => "Unterabschnitt",
        CommandNodeKind.Bereitstellung => "Bereitstellung",
        CommandNodeKind.Partner => "Einsatzleitung",
        _ => "Abschnitt",
    };
}
