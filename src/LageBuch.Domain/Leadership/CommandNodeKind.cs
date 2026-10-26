namespace LageBuch.Domain.Leadership;

/// <summary>What a <see cref="CommandNode"/> is. Members are never renamed: they travel by name on the wire.</summary>
public enum CommandNodeKind
{
    /// <summary>A top-level Einsatzabschnitt.</summary>
    Abschnitt,

    /// <summary>A part of an Abschnitt; always has an Abschnitt as parent.</summary>
    Unterabschnitt,

    /// <summary>A Bereitstellungsraum.</summary>
    Bereitstellung,

    /// <summary>The Einsatzleitung of a partner organisation (Polizei, THW, ...).</summary>
    Partner,
}
