namespace LageBuch.Persistence;

/// <summary>
/// The checks a folder under the system temp directory has to pass before attachment bytes go into
/// it in the clear: the upload spool (<see cref="IncidentFileStore"/>) and the copies "Öffnen"
/// hands to the OS (<c>AttachmentTempPaths</c>). The system temp directory is shared with every
/// other local user, who can create the folder first (#763). Here because Persistence is the
/// lowest layer that needs it.
/// </summary>
public static class PrivateTempFolder
{
    /// <summary>Owner-only (0700): what every such folder must be on Unix.</summary>
    public const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// Creates <paramref name="path"/> when it is missing and makes sure it is ours. A folder that
    /// is a symbolic link is refused with an <see cref="IOException"/>, because the bytes would
    /// land wherever it points. On Unix a folder found wider than owner-only is tightened; one that
    /// another user owns cannot be, and that fails with <see cref="UnauthorizedAccessException"/>.
    /// On Windows the per-user temp directory's ACL already keeps other users out.
    /// </summary>
    public static void Ensure(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Create(path);

        if (new DirectoryInfo(path).LinkTarget is not null)
        {
            // Stryker disable once String : callers act on the exception type; the text is only for a reader of a trace.
            throw new IOException("Der temporäre Ordner von Lagebuch ist eine Verknüpfung und wird nicht verwendet.");
        }

        // Stryker disable once Logical : on Unix, setting the mode a folder already has changes nothing.
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != OwnerOnly)
        {
            File.SetUnixFileMode(path, OwnerOnly);
        }
    }

    /// <summary>Creates <paramref name="path"/> owner-only on Unix, and as is on Windows.</summary>
    public static void Create(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (OperatingSystem.IsWindows())
        {
            // Stryker disable once Statement : runs on Windows only; the mutation runners are Linux.
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, OwnerOnly);
        }
    }
}
