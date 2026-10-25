using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.Files;
using LageBuch.Persistence;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// The private temp area "Öffnen" copies an attachment into before handing it to the OS, and the
/// gate both heads' <see cref="IFileDialogService.OpenFileAsync"/> check before launching. Both
/// halves live here so the writer and the launcher can never disagree on what counts as "one of
/// our own temp files". The Android head checks the gate before it hands its copy to a
/// <c>FileProvider</c>, and refuses to share anything inside <see cref="Root"/> that fails it.
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
    /// <summary>
    /// The single folder name under the temp root that attachment copies live in. Shared because
    /// three places have to agree on it and none of them can see the others' copy of the literal:
    /// <see cref="Root"/> here, the Android head's <c>EnsureShareable</c> (which recognises this
    /// tree as already shareable and skips a second copy), and the <c>attachments</c> entry in the
    /// Android head's <c>file_paths.xml</c>, which is what makes the FileProvider willing to hand
    /// out a URI for it at all. XML cannot reference this constant, so that file carries a comment
    /// pointing here; the other two use it directly. Its value comes from
    /// <see cref="IncidentFileStore.TempFolderName"/>, whose upload spool lives in the same folder.
    /// </summary>
    public const string RootFolderName = IncidentFileStore.TempFolderName;

    // Owner-only on Unix: the copies are decrypted attachment bytes, and the system temp directory
    // is shared with every other local user.
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>The one directory attachment copies may live in.</summary>
    public static string Root { get; } = Path.Join(Path.GetTempPath(), RootFolderName);

    /// <summary>
    /// How long a directory under <see cref="Root"/> must have gone unwritten before
    /// <see cref="SweepStaleOpenDirectories"/> removes it.
    /// </summary>
    public static TimeSpan StaleAfter { get; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Creates and returns a fresh, empty directory under <see cref="Root"/> for a single "Öffnen".
    /// On Unix the root and the new directory are owner-only (0700), and a root found wider than
    /// that is tightened. A root that is a symbolic link is refused with an
    /// <see cref="IOException"/>, because the copies would land wherever it points.
    /// </summary>
    public static string CreateOpenDirectory() => CreateOpenDirectory(Root);

    internal static string CreateOpenDirectory(string root)
    {
        CreatePrivateDirectory(root);

        if (new DirectoryInfo(root).LinkTarget is not null)
        {
            throw new IOException("Der Ordner für geöffnete Anhänge ist eine Verknüpfung und wird nicht verwendet.");
        }

        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(root) != OwnerOnly)
        {
            File.SetUnixFileMode(root, OwnerOnly);
        }

        var directory = Path.Join(root, Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        return directory;
    }

    // On Windows the per-user temp directory's ACL already keeps other users out.
    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, OwnerOnly);
        }
    }

    /// <summary>
    /// Deletes every directory <see cref="CreateOpenDirectory()"/> ever left under <see cref="Root"/>.
    /// Nothing else removes them, and Windows never empties its temp directory on its own, so
    /// without this every attachment ever opened would stay behind as an unencrypted copy (#383).
    /// <para>
    /// Meant for startup, not exit: a crash never reaches an exit handler, and a crashed run's
    /// leftovers are exactly what wants cleaning. Best-effort and never throws — a copy still held
    /// open by a viewer cannot be deleted on Windows, so that directory is skipped and removed on a
    /// later start instead. There is no age grace: a second instance started while a first one is
    /// running may remove the first one's copies too, which is accepted.
    /// </para>
    /// </summary>
    public static void SweepOpenDirectories() => SweepOpenDirectories(Root);

    /// <summary>
    /// <see cref="SweepOpenDirectories()"/> with an age grace: only directories last written more
    /// than <see cref="StaleAfter"/> ago go. For Android, whose Activity is created again on every
    /// rotation while a viewer may still be reading a fresh copy through a <c>content://</c> grant;
    /// nothing locks that copy, so without the grace it would be deleted under the viewer.
    /// </summary>
    public static void SweepStaleOpenDirectories(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        SweepOpenDirectories(Root, timeProvider.GetUtcNow().UtcDateTime - StaleAfter);
    }

    internal static void SweepOpenDirectories(string root, DateTime? modifiedBefore = null)
    {
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No root yet (DirectoryNotFoundException is an IOException) or not ours to read.
            return;
        }

        foreach (var directory in directories)
        {
            // Only what CreateOpenDirectory names; anything else under the root is not ours to judge.
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _))
            {
                continue;
            }

            try
            {
                if (modifiedBefore is { } cutoff && Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                {
                    continue;
                }

                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still open in a viewer, most likely; the next start tries again.
            }
        }

        SweepUploadSpools(root, modifiedBefore);
    }

    /// <summary>
    /// An upload spool deletes itself on close, so one left here is a crashed run's: a client's
    /// photo in the clear. One still in use is held open with no sharing on Windows and skipped;
    /// on Unix deleting it only unlinks the name and the upload carries on.
    /// </summary>
    private static void SweepUploadSpools(string root, DateTime? modifiedBefore)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(root, IncidentFileStore.SpoolFilePrefix + "*" + IncidentFileStore.SpoolFileExtension);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(name[IncidentFileStore.SpoolFilePrefix.Length..], "N", out _))
            {
                continue;
            }

            try
            {
                if (modifiedBefore is { } cutoff && File.GetLastWriteTimeUtc(file) >= cutoff)
                {
                    continue;
                }

                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still being uploaded; the next start tries again.
            }
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> resolves to a location strictly below <see cref="Root"/>,
    /// whether or not anything exists there. The Android share sheet uses it to tell an attachment
    /// copy, which has to pass <see cref="IsOpenableAttachment"/>, from an export it shares as is.
    /// </summary>
    public static bool IsInsideRoot(string? path) => TryResolveInsideRoot(path, out _);

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
    public static bool IsOpenableAttachment(string? path)
    {
        if (!TryResolveInsideRoot(path, out var full))
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

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A gate answers yes/no: an unresolvable path (NUL byte, over-long, permission) is simply not one of ours.")]
    private static bool TryResolveInsideRoot(string? path, [NotNullWhen(true)] out string? full)
    {
        full = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string resolved;
        try
        {
            resolved = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        var rootPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Root)) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, PathComparison))
        {
            return false;
        }

        full = resolved;
        return true;
    }

    // Windows and macOS resolve paths case-insensitively; Linux does not.
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
