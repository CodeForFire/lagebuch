namespace LageBuch.AppLogic.Services;

/// <summary>
/// The private temp area "Öffnen" copies an attachment into before handing it to the OS, and the
/// gate every platform's <see cref="IFileDialogService.OpenFileAsync"/> checks before launching.
/// Both halves live here so the writer and the launcher can never disagree on what counts as
/// "one of our own temp files".
/// <para>
/// It exists because attachment names are attacker-controlled: a joined sync client picks the name
/// in <c>AddFileCommand</c> and every peer writes those bytes to disk under it. The domain strips
/// path segments off the name (<c>IncidentFile</c>), and this keeps the blast radius of anything
/// that slips past to a directory of our own — one fresh directory per open, so two incidents
/// carrying the same file name also stop overwriting each other's copy.
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
    /// True only for an existing regular file (no symlink, no directory) that resolves to a
    /// location inside <see cref="Root"/> — the precondition for handing a path to the OS's default
    /// handler, which will happily launch executables and follow links.
    /// </summary>
    public static bool IsOpenableAttachment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);
        var rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, PathComparison))
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
