using LageBuch.Domain;
using LageBuch.Domain.Files;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class IncidentFileStoreTests : IDisposable
{
    private const string Stem = "20260622-0900-B";

    private readonly string _dir = Path.Join(Path.GetTempPath(), $"filestore-{Guid.NewGuid():N}");
    private readonly string _incidentPath;
    private readonly IncidentFileStore _store = new();

    public IncidentFileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _incidentPath = Path.Join(_dir, Stem + ".lagebuch");
        IncidentRepository.Save(_incidentPath, NewIncident());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static Incident NewIncident() =>
        Incident.Start(new FixedClock(), new SessionOperator("Test", null));

    private int BlobRowCount()
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(_incidentPath);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM incident_file_blobs;";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Saved_bytes_round_trip_through_the_incident_file()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", bytes);
        var read = await _store.TryReadBytesAsync(_incidentPath, "abc.jpg");

        Assert.Equal(bytes, read);
    }

    [Fact]
    public async Task Saving_bytes_creates_no_files_folder_next_to_the_incident()
    {
        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 1 });

        Assert.False(Directory.Exists(Path.Join(_dir, Stem + ".files")));
    }

    [Fact]
    public async Task Saving_the_same_storage_name_twice_replaces_the_bytes()
    {
        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 1, 2, 3 });

        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 9, 9 });

        Assert.Equal(new byte[] { 9, 9 }, await _store.TryReadBytesAsync(_incidentPath, "abc.jpg"));
        Assert.Equal(1, BlobRowCount());
    }

    [Fact]
    public async Task A_streamed_upload_without_a_known_length_lands_in_the_incident_file()
    {
        var bytes = new byte[300_000];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        await using var source = new NonSeekableStream(bytes);

        await _store.SaveStreamAsync(_incidentPath, "big.bin", source);

        Assert.Equal(bytes, await _store.TryReadBytesAsync(_incidentPath, "big.bin"));
    }

    [Fact]
    public async Task A_seekable_stream_is_stored_from_its_current_position()
    {
        using var source = new MemoryStream(new byte[] { 0, 0, 5, 6, 7 }) { Position = 2 };

        await _store.SaveStreamAsync(_incidentPath, "s.bin", source);

        Assert.Equal(new byte[] { 5, 6, 7 }, await _store.TryReadBytesAsync(_incidentPath, "s.bin"));
    }

    [Fact]
    public async Task A_streamed_upload_over_the_size_cap_is_rejected_and_stores_nothing()
    {
        await using var source = new NonSeekableStream(new byte[IncidentFile.MaxSizeBytes + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveStreamAsync(_incidentPath, "huge.bin", source));

        Assert.Equal(0, BlobRowCount());
    }

    [Fact]
    public async Task A_streamed_upload_of_exactly_the_size_cap_is_accepted()
    {
        await using var source = new NonSeekableStream(new byte[IncidentFile.MaxSizeBytes]);

        await _store.SaveStreamAsync(_incidentPath, "exact.bin", source);

        var stored = await _store.TryReadBytesAsync(_incidentPath, "exact.bin");

        Assert.NotNull(stored);
        Assert.Equal((int)IncidentFile.MaxSizeBytes, stored.Length);
    }

    [Fact]
    public async Task An_empty_attachment_is_stored_as_an_empty_row()
    {
        await _store.SaveBytesAsync(_incidentPath, "empty.bin", Array.Empty<byte>());

        Assert.Empty(await _store.TryReadBytesAsync(_incidentPath, "empty.bin") ?? [1]);
        Assert.Equal(1, BlobRowCount());
    }

    [Fact]
    public void A_source_shorter_than_the_announced_length_is_rejected_and_rolls_back()
    {
        using var cn = IncidentFileBlobs.OpenForWrite(_incidentPath);
        using (var tx = cn.BeginTransaction())
        {
            using var source = new MemoryStream(new byte[] { 1, 2 });

            Assert.Throws<EndOfStreamException>(() => IncidentFileBlobs.Write(cn, tx, "short.bin", source, 5));
        }

        Assert.Equal(0, BlobRowCount());
    }

    [Fact]
    public async Task An_incident_save_does_not_drop_stored_attachment_bytes()
    {
        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 4, 2 });

        var incident = NewIncident();
        IncidentRepository.Save(_incidentPath, incident);
        IncidentRepository.Save(_incidentPath, incident);

        Assert.Equal(new byte[] { 4, 2 }, await _store.TryReadBytesAsync(_incidentPath, "abc.jpg"));
    }

    [Fact]
    public async Task Deleting_bytes_removes_their_row()
    {
        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 1 });

        await _store.DeleteBytesAsync(_incidentPath, "abc.jpg");

        Assert.Null(await _store.TryReadBytesAsync(_incidentPath, "abc.jpg"));
        Assert.Equal(0, BlobRowCount());
    }

    private static readonly byte[] SecretMarker = System.Text.Encoding.ASCII.GetBytes("LAGEBUCH-SECRET-");

    private static byte[] MarkerPayload(byte[] marker)
    {
        var payload = new byte[marker.Length * 4096];
        for (var i = 0; i < 4096; i++)
        {
            marker.CopyTo(payload, i * marker.Length);
        }

        return payload;
    }

    private async Task AssertMarkerIsGoneAsync(byte[] marker)
    {
        Assert.Equal(-1, (await File.ReadAllBytesAsync(_incidentPath)).AsSpan().IndexOf(marker));
        var wal = _incidentPath + "-wal";
        if (File.Exists(wal))
        {
            Assert.Equal(-1, (await File.ReadAllBytesAsync(wal)).AsSpan().IndexOf(marker));
        }
    }

    [Fact]
    public async Task Deleted_attachment_bytes_are_not_left_in_the_incident_file()
    {
        await _store.SaveBytesAsync(_incidentPath, "secret.bin", MarkerPayload(SecretMarker));
        await _store.DeleteBytesAsync(_incidentPath, "secret.bin");

        await AssertMarkerIsGoneAsync(SecretMarker);
    }

    [Fact]
    public async Task Replaced_attachment_bytes_are_not_left_in_the_incident_file()
    {
        await _store.SaveBytesAsync(_incidentPath, "secret.bin", MarkerPayload(SecretMarker));
        await _store.SaveBytesAsync(_incidentPath, "secret.bin", MarkerPayload(System.Text.Encoding.ASCII.GetBytes("another-payload!")));

        await AssertMarkerIsGoneAsync(SecretMarker);
    }

    [Fact]
    public async Task Saving_runs_off_the_callers_thread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        using var source = new ThreadRecordingStream([1, 2, 3]);

        await _store.SaveStreamAsync(_incidentPath, "t.bin", source);

        Assert.NotEqual(0, source.ReadThreadId);
        Assert.NotEqual(callerThread, source.ReadThreadId);
    }

    [Fact]
    public async Task A_file_stamped_current_without_the_blob_table_still_accepts_bytes()
    {
        await using (var cn = SqliteConnectionFactory.OpenExisting(_incidentPath))
        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE incident_file_blobs;";
            await cmd.ExecuteNonQueryAsync();
        }

        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 3 });

        Assert.Equal(new byte[] { 3 }, await _store.TryReadBytesAsync(_incidentPath, "abc.jpg"));
    }

    [Fact]
    public async Task Reading_bytes_that_were_never_written_returns_null()
    {
        Assert.Null(await _store.TryReadBytesAsync(_incidentPath, "missing.jpg"));
    }

    [Fact]
    public async Task Reading_from_a_missing_incident_file_returns_null_rather_than_throwing()
    {
        var missing = Path.Join(_dir, "nope.lagebuch");

        Assert.Null(await _store.TryReadBytesAsync(missing, "abc.jpg"));
        Assert.False(File.Exists(missing));
    }

    [Fact]
    public async Task Stored_bytes_over_the_read_cap_are_not_loaded()
    {
        var store = new IncidentFileStore(maxReadBytes: 4, spoolDirectory: _dir);
        await store.SaveBytesAsync(_incidentPath, "big.jpg", new byte[] { 1, 2, 3, 4, 5 });

        Assert.Null(await store.TryReadBytesAsync(_incidentPath, "big.jpg"));
    }

    [Fact]
    public async Task Stored_bytes_exactly_at_the_read_cap_are_loaded()
    {
        var store = new IncidentFileStore(maxReadBytes: 4, spoolDirectory: _dir);
        await store.SaveBytesAsync(_incidentPath, "edge.jpg", new byte[] { 1, 2, 3, 4 });

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await store.TryReadBytesAsync(_incidentPath, "edge.jpg"));
    }

    [Fact]
    public async Task The_upload_spool_is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // The per-user temp directory's ACL keeps other users out there.
        }

        var spoolRoot = Path.Join(_dir, "spool");
        var store = new IncidentFileStore(IncidentFile.MaxSizeBytes, spoolRoot);
        var source = new SpoolInspectingStream(new byte[] { 1, 2, 3 }, spoolRoot);

        await store.SaveStreamAsync(_incidentPath, "spool.jpg", source);

        var mode = Assert.Single(source.SpoolModes);
        Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(spoolRoot));
        Assert.Empty(Directory.GetFiles(spoolRoot, "*", SearchOption.AllDirectories));
    }

    // Under the app's own temp folder, where the startup sweep finds what a crash left behind.
    [Fact]
    public async Task The_upload_spool_lives_in_the_lagebuch_temp_folder()
    {
        var spoolRoot = Path.Join(Path.GetTempPath(), "lagebuch");
        var source = new SpoolInspectingStream(new byte[] { 4, 5 }, spoolRoot);

        await _store.SaveStreamAsync(_incidentPath, "default.jpg", source);

        Assert.NotEmpty(source.SpoolPaths);
        Assert.All(source.SpoolPaths, p => Assert.StartsWith("upload-", Path.GetFileName(p), StringComparison.Ordinal));
    }

    /// <summary>Records the mode of every spool file present while the upload is being read.</summary>
    private sealed class SpoolInspectingStream(byte[] bytes, string spoolRoot) : MemoryStream(bytes)
    {
        private bool _inspected;

        public List<string> SpoolPaths { get; } = [];

        public List<UnixFileMode> SpoolModes { get; } = [];

        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_inspected)
            {
                _inspected = true;
                SpoolPaths.AddRange(Directory.GetFiles(spoolRoot, "upload-*.tmp"));
                if (!OperatingSystem.IsWindows())
                {
                    SpoolModes.AddRange(SpoolPaths.Select(File.GetUnixFileMode));
                }
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now { get; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.Zero);
    }

    private sealed class ThreadRecordingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int ReadThreadId { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ReadThreadId = Environment.CurrentManagedThreadId;
            return base.Read(buffer, offset, count);
        }
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
