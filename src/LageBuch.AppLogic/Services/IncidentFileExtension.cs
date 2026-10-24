namespace LageBuch.AppLogic.Services;

/// <summary>
/// The file extension an Einsatzdatei is saved under, and the one it used to be. New files are
/// <c>.lagebuch</c>; files written before that are <c>.fwincident</c> and still open, which is why
/// the open picker and every installer's file association accept both.
/// </summary>
public static class IncidentFileExtension
{
    /// <summary>The extension a new Einsatzdatei is saved under.</summary>
    public const string Current = ".lagebuch";

    /// <summary>The extension Einsatzdateien had before <see cref="Current"/>; still opened.</summary>
    public const string Legacy = ".fwincident";

    /// <summary>Whether <paramref name="path"/> names an Einsatzdatei, by either extension, in any case.</summary>
    public static bool IsIncidentFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var extension = Path.GetExtension(path);
        return string.Equals(extension, Current, StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, Legacy, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The Einsatzdatei the desktop was asked to open at launch -- what a double-click on a file
    /// passes as an argument -- or <c>null</c> when no argument names one.
    /// </summary>
    public static string? StartupFile(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.FirstOrDefault(IsIncidentFile);
    }
}
