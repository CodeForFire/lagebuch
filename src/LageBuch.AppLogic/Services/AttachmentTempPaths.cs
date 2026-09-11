using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.Files;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The private temp area "Öffnen" copies an attachment into before handing it to the OS, and the
/// gate the desktop head's <see cref="IFileDialogService.OpenFileAsync"/> checks before launching.
/// Both halves live here so the writer and the launcher can never disagree on what counts as
/// "one of our own temp files". The Android head hands its copy to a <c>FileProvider</c> and does
/// not consult this gate yet — hardening it is separate work.
/// <para>
/// It exists because attachment names are attacker-controlled: a joined sync client picks the name
/// in <c>AddFileCommand</c> and every peer writes those bytes to disk under it. The domain strips
/// path segments off the name and ties the extension to the content type
/// (<see cref="IncidentFile"/>), and this keeps the blast radius of anything that slips past — a
/// row that predates those rules, say — to a directory of our own: one fresh directory per open, so
/// two incidents carrying the same file name also stop overwriting each other's copy.
/// </para>
/// </summary>
public static class AttachmentTempPaths
{
    /// <summary>The one directory attachment copies may live in.</summary>
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "lagebuch");

    /// <summary>
    /// Creates and returns a fresh, empty directory under <see cref="Root"/> for a single "Öffnen".
    /// </summary>
    public static string CreateOpenDirectory()
    {
        var directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// True only for an existing regular file (no symlink, no directory) that resolves to a location
    /// inside <see cref="Root"/> and carries one of <see cref="IncidentFile.MimeTypesByExtension"/>'s
    /// extensions — the precondition for handing a path to the OS's default handler, which will
    /// happily launch <c>.hta</c>, <c>.js</c>, <c>.lnk</c> or <c>.desktop</c> and follow links.
    /// <para>
    /// Residual, deliberately not covered: only the final component is checked for being a link, so
    /// an intermediate symlinked directory inside <see cref="Root"/> would still resolve outward.
    /// Nothing but this app writes into that root, and creating one there already requires local
    /// write access to the user's own temp directory.
    /// </para>
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A gate answers yes/no: an unresolvable path (NUL byte, over-long, permission) is simply not one of ours.")]
    public static bool IsOpenableAttachment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, PathComparison))
        {
            return false;
        }

        if (!IncidentFile.MimeTypesByExtension.ContainsKey(Path.GetExtension(full)))
        {
            return false;
        }

        var file = new FileInfo(full);
        return file.Exists && file.LinkTarget is null;
    }

    // Windows and macOS resolve paths case-insensitively; Linux does not.
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
