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
    private readonly Incident _incident = NewIncident();

    public IncidentFileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _incidentPath = Path.Join(_dir, Stem + ".lagebuch");
        IncidentRepository.Save(_incidentPath, _incident);
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

    /// <summary>Saves an attachment row, as every caller does before its bytes, and returns the
    /// storage name the bytes go under. The row is a JPEG whatever the name says; the store never
    /// looks at the type.</summary>
    private string Attach(string fileName)
    {
        var file = _incident.AddFile(new FixedClock(), new SessionOperator("Test", null), Path.ChangeExtension(fileName, ".jpg"), "image/jpeg", 1);
        IncidentRepository.Save(_incidentPath, _incident);
        return IncidentFile.StorageFileName(file.Id, file.FileName);
    }

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
        var name = Attach("abc.jpg");
        var bytes = new byte[] { 1, 2, 3, 4 };

        await _store.SaveBytesAsync(_incidentPath, name, bytes);
        var read = await _store.TryReadBytesAsync(_incidentPath, name);

        Assert.Equal(bytes, read);
    }

    [Fact]
    public async Task Saving_bytes_creates_no_files_folder_next_to_the_incident()
    {
        var name = Attach("abc.jpg");
        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 1 });

        Assert.False(Directory.Exists(Path.Join(_dir, Stem + ".files")));
    }

    [Fact]
    public async Task Saving_the_same_storage_name_twice_replaces_the_bytes()
    {
        var name = Attach("abc.jpg");
        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 1, 2, 3 });

        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 9, 9 });

        Assert.Equal(new byte[] { 9, 9 }, await _store.TryReadBytesAsync(_incidentPath, name));
        Assert.Equal(1, BlobRowCount());
    }

    [Fact]
    public async Task A_streamed_upload_without_a_known_length_lands_in_the_incident_file()
    {
        var name = Attach("big.bin");
        var bytes = new byte[300_000];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        await using var source = new NonSeekableStream(bytes);

        await _store.SaveStreamAsync(_incidentPath, name, source);

        Assert.Equal(bytes, await _store.TryReadBytesAsync(_incidentPath, name));
    }

    [Fact]
    public async Task A_seekable_stream_is_stored_from_its_current_position()
    {
        var name = Attach("s.bin");
        using var source = new MemoryStream(new byte[] { 0, 0, 5, 6, 7 }) { Position = 2 };

        await _store.SaveStreamAsync(_incidentPath, name, source);

        Assert.Equal(new byte[] { 5, 6, 7 }, await _store.TryReadBytesAsync(_incidentPath, name));
    }

    [Fact]
    public async Task A_streamed_upload_over_the_size_cap_is_rejected_and_stores_nothing()
    {
        var name = Attach("huge.bin");
        await using var source = new NonSeekableStream(new byte[IncidentFile.MaxSizeBytes + 1]);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveStreamAsync(_incidentPath, name, source));

        Assert.Equal(0, BlobRowCount());
    }

    [Fact]
    public async Task A_streamed_upload_of_exactly_the_size_cap_is_accepted()
    {
        var name = Attach("exact.bin");
        await using var source = new NonSeekableStream(new byte[IncidentFile.MaxSizeBytes]);

        await _store.SaveStreamAsync(_incidentPath, name, source);

        var stored = await _store.TryReadBytesAsync(_incidentPath, name);

        Assert.NotNull(stored);
        Assert.Equal((int)IncidentFile.MaxSizeBytes, stored.Length);
    }

    [Fact]
    public async Task An_empty_attachment_is_stored_as_an_empty_row()
    {
        var name = Attach("empty.bin");
        await _store.SaveBytesAsync(_incidentPath, name, Array.Empty<byte>());

        Assert.Empty(await _store.TryReadBytesAsync(_incidentPath, name) ?? [1]);
        Assert.Equal(1, BlobRowCount());
    }

    [Fact]
    public void A_source_shorter_than_the_announced_length_is_rejected_and_rolls_back()
    {
        var name = Attach("short.bin");
        using var cn = IncidentFileBlobs.OpenForWrite(_incidentPath);
        using (var tx = cn.BeginTransaction())
        {
            using var source = new MemoryStream(new byte[] { 1, 2 });

            Assert.Throws<EndOfStreamException>(() => IncidentFileBlobs.Write(cn, tx, name, source, 5));
        }

        Assert.Equal(0, BlobRowCount());
    }

    [Fact]
    public async Task An_incident_save_does_not_drop_stored_attachment_bytes()
    {
        var name = Attach("abc.jpg");
        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 4, 2 });

        IncidentRepository.Save(_incidentPath, _incident);
        IncidentRepository.Save(_incidentPath, _incident);

        Assert.Equal(new byte[] { 4, 2 }, await _store.TryReadBytesAsync(_incidentPath, name));
    }

    [Fact]
    public async Task Deleting_bytes_removes_their_row()
    {
        var name = Attach("abc.jpg");
        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 1 });

        await _store.DeleteBytesAsync(_incidentPath, name);

        Assert.Null(await _store.TryReadBytesAsync(_incidentPath, name));
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
        var name = Attach("secret.bin");
        await _store.SaveBytesAsync(_incidentPath, name, MarkerPayload(SecretMarker));
        await _store.DeleteBytesAsync(_incidentPath, name);

        await AssertMarkerIsGoneAsync(SecretMarker);
    }

    [Fact]
    public async Task Replaced_attachment_bytes_are_not_left_in_the_incident_file()
    {
        var name = Attach("secret.bin");
        await _store.SaveBytesAsync(_incidentPath, name, MarkerPayload(SecretMarker));
        await _store.SaveBytesAsync(_incidentPath, name, MarkerPayload(System.Text.Encoding.ASCII.GetBytes("another-payload!")));

        await AssertMarkerIsGoneAsync(SecretMarker);
    }

    [Fact]
    public async Task Saving_runs_off_the_callers_thread()
    {
        var name = Attach("t.bin");
        var callerThread = Environment.CurrentManagedThreadId;
        using var source = new ThreadRecordingStream([1, 2, 3]);

        await _store.SaveStreamAsync(_incidentPath, name, source);

        Assert.NotEqual(0, source.ReadThreadId);
        Assert.NotEqual(callerThread, source.ReadThreadId);
    }

    [Fact]
    public async Task A_file_stamped_current_without_the_blob_table_still_accepts_bytes()
    {
        var name = Attach("abc.jpg");
        await using (var cn = SqliteConnectionFactory.OpenExisting(_incidentPath))
        await using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "DROP TABLE incident_file_blobs;";
            await cmd.ExecuteNonQueryAsync();
        }

        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 3 });

        Assert.Equal(new byte[] { 3 }, await _store.TryReadBytesAsync(_incidentPath, name));
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
        var name = Attach("big.jpg");
        var store = new IncidentFileStore(maxReadBytes: 4, spoolDirectory: _dir);
        await store.SaveBytesAsync(_incidentPath, name, new byte[] { 1, 2, 3, 4, 5 });

        Assert.Null(await store.TryReadBytesAsync(_incidentPath, name));
    }

    [Fact]
    public async Task Stored_bytes_exactly_at_the_read_cap_are_loaded()
    {
        var name = Attach("edge.jpg");
        var store = new IncidentFileStore(maxReadBytes: 4, spoolDirectory: _dir);
        await store.SaveBytesAsync(_incidentPath, name, new byte[] { 1, 2, 3, 4 });

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await store.TryReadBytesAsync(_incidentPath, name));
    }

    [Fact]
    public async Task The_upload_spool_is_readable_by_its_owner_only()
    {
        var name = Attach("spool.jpg");
        if (OperatingSystem.IsWindows())
        {
            return; // The per-user temp directory's ACL keeps other users out there.
        }

        var spoolRoot = Path.Join(_dir, "spool");
        var store = new IncidentFileStore(IncidentFile.MaxSizeBytes, spoolRoot);
        var source = new SpoolInspectingStream(new byte[] { 1, 2, 3 }, spoolRoot);

        await store.SaveStreamAsync(_incidentPath, name, source);

        var mode = Assert.Single(source.SpoolModes);
        Assert.Equal(UnixFileMode.None, mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(spoolRoot));
        Assert.Empty(Directory.GetFiles(spoolRoot, "*", SearchOption.AllDirectories));
    }

    // #763: another local user plants the spool folder as a link before Lagebuch first creates it.
    // The upload must not spool a client's photo wherever the link points.
    [Fact]
    public async Task An_upload_refuses_a_spool_folder_that_is_a_symlink()
    {
        var name = Attach("link.jpg");
        var spoolRoot = Path.Join(_dir, "spool");
        var outside = Path.Join(_dir, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(spoolRoot, outside);
        var store = new IncidentFileStore(IncidentFile.MaxSizeBytes, spoolRoot);
        await using var source = new NonSeekableStream(new byte[] { 1, 2, 3 });

        await Assert.ThrowsAsync<IOException>(() => store.SaveStreamAsync(_incidentPath, name, source));

        Assert.Empty(Directory.GetFileSystemEntries(outside));
        Assert.Equal(0, BlobRowCount());
    }

    // #763: a spool folder an older build or another user left readable by others is tightened
    // before a spool goes into it.
    [Fact]
    public async Task An_upload_tightens_a_spool_folder_readable_by_others()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // The per-user temp directory's ACL keeps other users out there.
        }

        var name = Attach("wide.jpg");
        var spoolRoot = Path.Join(_dir, "spool");
        Directory.CreateDirectory(spoolRoot);
        File.SetUnixFileMode(spoolRoot, (UnixFileMode)0x1FF); // 0777
        var store = new IncidentFileStore(IncidentFile.MaxSizeBytes, spoolRoot);
        await using var source = new NonSeekableStream(new byte[] { 1, 2, 3 });

        await store.SaveStreamAsync(_incidentPath, name, source);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(spoolRoot));
    }

    // Under the app's own temp folder, where the startup sweep finds what a crash left behind.
    [Fact]
    public async Task The_upload_spool_lives_in_the_lagebuch_temp_folder()
    {
        var name = Attach("default.jpg");
        var spoolRoot = Path.Join(Path.GetTempPath(), "lagebuch");
        var source = new SpoolInspectingStream(new byte[] { 4, 5 }, spoolRoot);

        await _store.SaveStreamAsync(_incidentPath, name, source);

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

    private void RemoveAttachment()
    {
        _incident.RemoveFile(new FixedClock(), new SessionOperator("Test", null), _incident.Files.Single().Id);
        IncidentRepository.Save(_incidentPath, _incident);
    }

    // #761: a joined client's body is still being spooled when the attachment is removed. The
    // removal's own wipe finds nothing yet, so bytes stored afterwards would never be wiped.
    [Fact]
    public async Task An_upload_whose_attachment_is_removed_while_it_arrives_stores_nothing()
    {
        var name = Attach("race.jpg");
        await using var source = new RemovalRacingStream(MarkerPayload(SecretMarker), RemoveAttachment);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _store.SaveStreamAsync(_incidentPath, name, source));

        Assert.Equal(0, BlobRowCount());
        await AssertMarkerIsGoneAsync(SecretMarker);
    }

    [Fact]
    public async Task Bytes_for_a_name_no_attachment_has_are_refused()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _store.SaveBytesAsync(_incidentPath, "unknown.jpg", new byte[] { 1 }));

        Assert.Equal(0, BlobRowCount());
    }

    // #761: a crash, or a failed wipe, between a removal's two deletes leaves the bytes behind.
    [Fact]
    public async Task Opening_an_incident_wipes_bytes_no_attachment_names()
    {
        var name = Attach("left.bin");
        await _store.SaveBytesAsync(_incidentPath, name, MarkerPayload(SecretMarker));
        RemoveAttachment();

        IncidentRepository.Load(_incidentPath);

        Assert.Equal(0, BlobRowCount());
        await AssertMarkerIsGoneAsync(SecretMarker);
    }

    [Fact]
    public async Task Opening_an_incident_keeps_the_bytes_its_attachments_name()
    {
        var name = Attach("kept.bin");
        await _store.SaveBytesAsync(_incidentPath, name, new byte[] { 7 });

        IncidentRepository.Load(_incidentPath);

        Assert.Equal(new byte[] { 7 }, await _store.TryReadBytesAsync(_incidentPath, name));
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    /// <summary>A request body that runs <paramref name="onFirstRead"/> once, before its first bytes.</summary>
    private sealed class RemovalRacingStream(byte[] bytes, Action onFirstRead) : MemoryStream(bytes)
    {
        private bool _started;

        public override bool CanSeek => false;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_started)
            {
                _started = true;
                onFirstRead();
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
