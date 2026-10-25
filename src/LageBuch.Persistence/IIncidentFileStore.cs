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
    Task SaveBytesAsync(string incidentPath, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="source"/> without ever materializing the whole attachment in memory
    /// (issue #167 P1 #2) -- used for a joined client's upload, whose bytes arrive as a raw HTTP
    /// request body rather than a JSON-decoded <c>byte[]</c>. A stream over
    /// <see cref="IncidentFile.MaxSizeBytes"/> is rejected with <see cref="InvalidDataException"/>.
    /// </summary>
    Task SaveStreamAsync(string incidentPath, string storageFileName, Stream source, CancellationToken cancellationToken = default);

    /// <summary>Null on any failure (missing file, missing row) -- never throws, so a
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
        // so spool to a temp file that vanishes on close. Memory stays at the copy buffer.
        await using var spool = new FileStream(
            Path.Join(Path.GetTempPath(), $"lagebuch-upload-{Guid.NewGuid():N}.tmp"),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            CopyBufferSize,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
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
                    return Read(incidentPath, storageFileName);
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

    private static byte[]? Read(string incidentPath, string storageFileName)
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(incidentPath);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT bytes FROM " + IncidentFileBlobs.Table + " WHERE storage_name = $name;";
        cmd.Parameters.AddWithValue("$name", storageFileName);
        return cmd.ExecuteScalar() as byte[];
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
