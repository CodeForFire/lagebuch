using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain.Files;
using LageBuch.Persistence.Sqlite;

namespace LageBuch.Persistence;

/// <summary>
/// Stores an attached file's bytes inside the incident's own SQLite file, in
/// <c>incident_file_blobs</c>. That table sits outside the full rewrite
/// <see cref="IncidentRepository"/> does per save, so a save never moves attachment bytes; only
/// the metadata row (see <c>incident_files</c>) is rewritten.
/// </summary>
public interface IIncidentFileStore
{
    /// <summary>
    /// Stores bytes for an attachment whose <c>incident_files</c> row is already saved. Throws
    /// <see cref="KeyNotFoundException"/> and stores nothing when no attachment names
    /// <paramref name="storageFileName"/>, for instance because it was removed meanwhile (#761).
    /// The same holds for <see cref="SaveStreamAsync"/>.
    /// </summary>
    Task SaveBytesAsync(string incidentPath, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="source"/> without ever materializing the whole attachment in memory
    /// (issue #167 P1 #2) -- used for a joined client's upload, whose bytes arrive as a raw HTTP
    /// request body rather than a JSON-decoded <c>byte[]</c>. A stream over
    /// <see cref="IncidentFile.MaxSizeBytes"/> is rejected with <see cref="InvalidDataException"/>.
    /// </summary>
    Task SaveStreamAsync(string incidentPath, string storageFileName, Stream source, CancellationToken cancellationToken = default);

    /// <summary>Null on any failure (missing file, missing row, a row over
    /// <see cref="IncidentFile.MaxSizeBytes"/>) -- never throws, so a
    /// caller degrades quietly, matching <see cref="IncidentRepository.TryReadState"/>.</summary>
    Task<byte[]?> TryReadBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the row and overwrites its pages (<c>secure_delete</c>), so a deleted photo is not
    /// left readable inside a file that gets handed on. A failure propagates: the caller removing
    /// the metadata must know the bytes are still there.
    /// </summary>
    Task DeleteBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default);
}

public sealed class IncidentFileStore : IIncidentFileStore
{
    private const int CopyBufferSize = 81920;

    /// <summary>
    /// The folder under the system temp directory where this app keeps its private temp files:
    /// the upload spool here, and the copies "Öffnen" hands to the OS
    /// (<c>AttachmentTempPaths.Root</c>, whose startup sweep also removes a spool a crash left).
    /// Defined here because Persistence is the lowest layer that needs it.
    /// </summary>
    public const string TempFolderName = "lagebuch";

    /// <summary>A spool file is named <c>upload-{guid:N}.tmp</c>; the sweep matches exactly that.</summary>
    public const string SpoolFilePrefix = "upload-";

    /// <inheritdoc cref="SpoolFilePrefix"/>
    public const string SpoolFileExtension = ".tmp";

    private const UnixFileMode OwnerOnlyDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly long _maxReadBytes;
    private readonly string _spoolDirectory;

    public IncidentFileStore()
        : this(IncidentFile.MaxSizeBytes, Path.Join(Path.GetTempPath(), TempFolderName))
    {
    }

    /// <summary>For tests: a smaller read cap than 25 MB, and a spool folder of their own.</summary>
    internal IncidentFileStore(long maxReadBytes, string spoolDirectory)
    {
        ArgumentNullException.ThrowIfNull(spoolDirectory);
        _maxReadBytes = maxReadBytes;
        _spoolDirectory = spoolDirectory;
    }

    // Every operation hands its SQLite work to the thread pool: the callers await these from the UI
    // thread, and 25 MB of WAL writes plus an fsync (or a SQLITE_BUSY wait behind IncidentStore's
    // writer) must never freeze it (#167 P0). Each operation owns its connection, so this is safe.
    public Task SaveBytesAsync(string incidentPath, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Task.Run(
            () =>
            {
                using var source = new MemoryStream(bytes, writable: false);
                Store(incidentPath, storageFileName, source, bytes.Length);
            },
            cancellationToken);
    }

    public async Task SaveStreamAsync(string incidentPath, string storageFileName, Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            await Task.Run(() => Store(incidentPath, storageFileName, source, source.Length - source.Position), cancellationToken);
            return;
        }

        // The length is unknown until the body ends and SQLite needs it up front for the zeroblob,
        // so spool to a temp file that vanishes on close. Memory stays at the copy buffer. The
        // spool holds a client's photo in the clear and the system temp directory is shared with
        // every other local user, so it is owner-only (0600) from the moment it exists.
        await using var spool = new FileStream(CreateSpoolPath(), SpoolOptions());
        var buffer = new byte[CopyBufferSize];
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (spool.Length + read > IncidentFile.MaxSizeBytes)
            {
                throw new InvalidDataException(IncidentFile.TooLargeMessage);
            }

            await spool.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        spool.Position = 0;
        await Task.Run(() => Store(incidentPath, storageFileName, spool, spool.Length), cancellationToken);
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Try-read: an unreadable attachment degrades to null, never fails incident load.")]
    public Task<byte[]?> TryReadBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default)
    {
        return Task.Run(
            () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Read(incidentPath, storageFileName, _maxReadBytes);
                }
                catch
                {
                    return null;
                }
            },
            CancellationToken.None);
    }

    public Task DeleteBytesAsync(string incidentPath, string storageFileName, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Remove(incidentPath, storageFileName), cancellationToken);
    }

    /// <summary>
    /// Null for a row over <paramref name="maxBytes"/>: a crafted file can hold a blob of up to
    /// 1 GB, which would otherwise be loaded whole and served to every joined client. SQLite reads
    /// a blob's <c>length()</c> from its header, so the check loads none of it.
    /// </summary>
    private static byte[]? Read(string incidentPath, string storageFileName, long maxBytes)
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(incidentPath);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT bytes FROM " + IncidentFileBlobs.Table + " WHERE storage_name = $name AND length(bytes) <= $max;";
        cmd.Parameters.AddWithValue("$name", storageFileName);
        cmd.Parameters.AddWithValue("$max", maxBytes);
        return cmd.ExecuteScalar() as byte[];
    }

    private static FileStreamOptions SpoolOptions()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.DeleteOnClose | FileOptions.Asynchronous,
            BufferSize = CopyBufferSize,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        return options;
    }

    private string CreateSpoolPath()
    {
        if (OperatingSystem.IsWindows())
        {
            // The per-user temp directory's ACL already keeps other users out.
            Directory.CreateDirectory(_spoolDirectory);
        }
        else
        {
            Directory.CreateDirectory(_spoolDirectory, OwnerOnlyDirectory);
        }

        return Path.Join(_spoolDirectory, $"{SpoolFilePrefix}{Guid.NewGuid():N}{SpoolFileExtension}");
    }

    private static void Remove(string incidentPath, string storageFileName)
    {
        using var cn = IncidentFileBlobs.OpenForWrite(incidentPath);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM " + IncidentFileBlobs.Table + " WHERE storage_name = $name;";
        cmd.Parameters.AddWithValue("$name", storageFileName);
        cmd.ExecuteNonQuery();
    }

    private static void Store(string incidentPath, string storageFileName, Stream source, long length)
    {
        using var cn = IncidentFileBlobs.OpenForWrite(incidentPath);
        using var tx = cn.BeginTransaction();
        IncidentFileBlobs.Write(cn, tx, storageFileName, source, length);
        tx.Commit();
    }
}
