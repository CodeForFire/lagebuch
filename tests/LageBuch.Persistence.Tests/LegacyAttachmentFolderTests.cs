using System.Globalization;
using LageBuch.Domain;
using LageBuch.Domain.Files;
using LageBuch.Domain.Time;
using LageBuch.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

public class LegacyAttachmentFolderTests : IDisposable
{
    private const string Stem = "20260622-0900-B";

    private readonly string _root = Path.Join(Path.GetTempPath(), $"legacyfiles-{Guid.NewGuid():N}");
    private readonly string _dir;
    private readonly IncidentFileStore _store = new();

    public LegacyAttachmentFolderTests()
    {
        _dir = Path.Join(_root, "inner");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string FolderFor(string path) => Path.Join(_dir, Path.GetFileNameWithoutExtension(path) + ".files");

    private (string Path, IncidentFile File) SaveWithLegacyFile(string extension, byte[] bytes, bool closed = false)
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        var file = incident.AddFile(clock, op, "foto.jpg", "image/jpeg", bytes.Length);
        if (closed)
        {
            incident.Close(clock, op);
        }

        var path = Path.Join(_dir, Stem + extension);
        IncidentRepository.Save(path, incident);
        var folder = FolderFor(path);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Join(folder, IncidentFile.StorageFileName(file.Id, file.FileName)), bytes);
        return (path, file);
    }

    private static int BlobRowCount(string path)
    {
        using var cn = SqliteConnectionFactory.OpenReadOnly(path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM incident_file_blobs;";
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Opening_a_legacy_incident_moves_its_attachments_into_the_file()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var (path, file) = SaveWithLegacyFile(".lagebuch", bytes);

        _ = IncidentRepository.Load(path);

        var storage = IncidentFile.StorageFileName(file.Id, file.FileName);
        Assert.Equal(bytes, await _store.TryReadBytesAsync(path, storage));
        Assert.False(Directory.Exists(FolderFor(path)));
    }

    [Fact]
    public async Task Opening_a_legacy_fwincident_imports_its_attachments_too()
    {
        var bytes = new byte[] { 7, 8, 9 };
        var (path, file) = SaveWithLegacyFile(".fwincident", bytes);

        _ = IncidentRepository.Load(path);

        var storage = IncidentFile.StorageFileName(file.Id, file.FileName);
        Assert.Equal(bytes, await _store.TryReadBytesAsync(path, storage));
        Assert.False(Directory.Exists(FolderFor(path)));
    }

    [Fact]
    public async Task Opening_a_legacy_abgeschlossen_incident_imports_it_too()
    {
        var bytes = new byte[] { 4, 4, 2 };
        var (path, file) = SaveWithLegacyFile(".lagebuch", bytes, closed: true);

        _ = IncidentRepository.Load(path);

        var storage = IncidentFile.StorageFileName(file.Id, file.FileName);
        Assert.Equal(bytes, await _store.TryReadBytesAsync(path, storage));
        Assert.False(Directory.Exists(FolderFor(path)));
    }

    [Fact]
    public async Task A_file_in_the_legacy_folder_that_no_attachment_names_is_left_in_place()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1 });
        var stray = Path.Join(FolderFor(path), "notes.txt");
        await File.WriteAllTextAsync(stray, "keep me");

        _ = IncidentRepository.Load(path);

        var storage = IncidentFile.StorageFileName(file.Id, file.FileName);
        Assert.True(File.Exists(stray));
        Assert.Equal(1, BlobRowCount(path));
        Assert.NotNull(await _store.TryReadBytesAsync(path, storage));
        Assert.False(File.Exists(Path.Join(FolderFor(path), storage)));
    }

    [Fact]
    public void Opening_twice_does_not_import_or_fail_again()
    {
        var (path, _) = SaveWithLegacyFile(".lagebuch", new byte[] { 1, 2 });
        _ = IncidentRepository.Load(path);
        var before = BlobRowCount(path);

        _ = IncidentRepository.Load(path);

        Assert.Equal(before, BlobRowCount(path));
        Assert.Equal(1, before);
    }

    [Fact]
    public async Task A_cleanup_that_cannot_delete_still_opens_and_a_later_open_finishes_it()
    {
        var bytes = new byte[] { 5, 5, 5 };
        var (path, file) = SaveWithLegacyFile(".lagebuch", bytes);
        var folder = FolderFor(path);
        var storage = IncidentFile.StorageFileName(file.Id, file.FileName);
        var legacy = Path.Join(folder, storage);

        if (OperatingSystem.IsWindows())
        {
            // Readable but not deletable: no FileShare.Delete.
            using (new FileStream(legacy, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _ = IncidentRepository.Load(path);
                Assert.Equal(bytes, await _store.TryReadBytesAsync(path, storage));
                Assert.True(File.Exists(legacy));
            }
        }
        else
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                _ = IncidentRepository.Load(path);
                Assert.Equal(bytes, await _store.TryReadBytesAsync(path, storage));
                Assert.True(File.Exists(legacy));
            }
            finally
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        _ = IncidentRepository.Load(path);

        Assert.False(Directory.Exists(folder));
        Assert.Equal(bytes, await _store.TryReadBytesAsync(path, storage));
    }

    [Theory]
    [InlineData("x./../../victim")]
    [InlineData("x.\\..\\..\\victim")]
    public async Task A_hostile_file_name_cannot_make_the_import_reach_outside_the_folder(string hostileName)
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1 });
        File.Delete(Path.Join(FolderFor(path), IncidentFile.StorageFileName(file.Id, file.FileName)));
        var victimBeside = Path.Join(_dir, "victim");
        var victimAbove = Path.Join(_root, "victim");
        await File.WriteAllTextAsync(victimBeside, "secret");
        await File.WriteAllTextAsync(victimAbove, "secret");
        using (var cn = SqliteConnectionFactory.OpenExisting(path))
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "UPDATE incident_files SET file_name = $n;";
            cmd.Parameters.AddWithValue("$n", hostileName);
            await cmd.ExecuteNonQueryAsync();
        }

        _ = IncidentRepository.Load(path);

        Assert.True(File.Exists(victimBeside));
        Assert.True(File.Exists(victimAbove));
        Assert.Equal(0, BlobRowCount(path));
    }

    [Fact]
    public void An_incident_without_a_legacy_folder_opens_unchanged()
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Test", null);
        var path = Path.Join(_dir, Stem + ".lagebuch");
        IncidentRepository.Save(path, Incident.Start(clock, op));

        _ = IncidentRepository.Load(path);

        Assert.Equal(0, BlobRowCount(path));
        Assert.False(Directory.Exists(FolderFor(path)));
    }

    private (string Path, IncidentFile First, IncidentFile Second) SaveWithTwoLegacyFiles(byte[] first, byte[] second)
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Test", null);
        var incident = Incident.Start(clock, op);
        var a = incident.AddFile(clock, op, "a.jpg", "image/jpeg", first.Length);
        var b = incident.AddFile(clock, op, "b.jpg", "image/jpeg", second.Length);
        var path = Path.Join(_dir, Stem + ".lagebuch");
        IncidentRepository.Save(path, incident);
        var folder = FolderFor(path);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Join(folder, IncidentFile.StorageFileName(a.Id, a.FileName)), first);
        File.WriteAllBytes(Path.Join(folder, IncidentFile.StorageFileName(b.Id, b.FileName)), second);
        return (path, a, b);
    }

    [Fact]
    public async Task An_unreadable_legacy_file_does_not_block_opening_and_the_rest_is_imported()
    {
        var good = new byte[] { 1, 2, 3 };
        var (path, first, second) = SaveWithTwoLegacyFiles(good, new byte[] { 9, 9 });
        var folder = FolderFor(path);
        var goodName = IncidentFile.StorageFileName(first.Id, first.FileName);
        var badName = IncidentFile.StorageFileName(second.Id, second.FileName);
        var bad = Path.Join(folder, badName);

        await using var locked = OperatingSystem.IsWindows()
            ? new FileStream(bad, FileMode.Open, FileAccess.Read, FileShare.None)
            : null;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(bad, UnixFileMode.None);
        }

        try
        {
            _ = IncidentRepository.Load(path);

            Assert.Equal(good, await _store.TryReadBytesAsync(path, goodName));
            Assert.False(File.Exists(Path.Join(folder, goodName)));
            Assert.True(File.Exists(bad));
            Assert.Null(await _store.TryReadBytesAsync(path, badName));
            Assert.Equal(1, BlobRowCount(path));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(bad, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
    }

    [Fact]
    public async Task A_legacy_file_that_is_a_symlink_is_never_copied_into_the_incident()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1 });
        var legacy = Path.Join(FolderFor(path), IncidentFile.StorageFileName(file.Id, file.FileName));
        var secret = Path.Join(_root, "secret.txt");
        await File.WriteAllTextAsync(secret, "private key");
        File.Delete(legacy);
        try
        {
            File.CreateSymbolicLink(legacy, secret);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _ = IncidentRepository.Load(path);

        Assert.Equal(0, BlobRowCount(path));
        Assert.True(File.Exists(secret));
        Assert.NotNull(new FileInfo(legacy).LinkTarget);
    }

    [Fact]
    public void A_legacy_folder_that_is_a_symlink_is_skipped_entirely()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1 });
        var folder = FolderFor(path);
        var elsewhere = Path.Join(_root, "elsewhere");
        Directory.Move(folder, elsewhere);
        try
        {
            Directory.CreateSymbolicLink(folder, elsewhere);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _ = IncidentRepository.Load(path);

        Assert.Equal(0, BlobRowCount(path));
        Assert.True(File.Exists(Path.Join(elsewhere, IncidentFile.StorageFileName(file.Id, file.FileName))));
    }

    [Fact]
    public async Task A_stored_row_of_a_different_length_does_not_licence_deleting_the_legacy_file()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1, 2, 3 });
        var storage = IncidentFile.StorageFileName(file.Id, file.FileName);
        await _store.SaveBytesAsync(path, storage, new byte[] { 7 });

        _ = IncidentRepository.Load(path);

        Assert.True(File.Exists(Path.Join(FolderFor(path), storage)));
        Assert.Equal(new byte[] { 7 }, await _store.TryReadBytesAsync(path, storage));
    }

    [Fact]
    public void A_legacy_file_over_the_cap_is_skipped_and_left_on_disk()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1, 2, 3, 4, 5 });
        var legacy = Path.Join(FolderFor(path), IncidentFile.StorageFileName(file.Id, file.FileName));

        using (var cn = SqliteConnectionFactory.OpenExisting(path))
        {
            LegacyAttachmentFolder.Import(cn, path, maxSizeBytes: 4);
        }

        Assert.Equal(0, BlobRowCount(path));
        Assert.True(File.Exists(legacy));
    }

    [Fact]
    public void A_legacy_file_exactly_at_the_cap_is_imported()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1, 2, 3, 4 });

        using (var cn = SqliteConnectionFactory.OpenExisting(path))
        {
            LegacyAttachmentFolder.Import(cn, path, maxSizeBytes: 4);
        }

        Assert.Equal(1, BlobRowCount(path));
        Assert.False(File.Exists(Path.Join(FolderFor(path), IncidentFile.StorageFileName(file.Id, file.FileName))));
    }

    [Fact]
    public void A_row_whose_id_is_not_a_guid_never_imports_a_file()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1 });
        var stray = Path.Join(FolderFor(path), IncidentFile.StorageFileName(Guid.Empty, file.FileName));
        File.WriteAllBytes(stray, new byte[] { 9 });
        using (var cn = SqliteConnectionFactory.OpenExisting(path))
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE incident_files SET id = 'not-a-guid';";
            cmd.ExecuteNonQuery();
            LegacyAttachmentFolder.Import(cn, path);
        }

        Assert.Equal(0, BlobRowCount(path));
        Assert.True(File.Exists(stray));
    }

    private static void SetRawFileName(string path, string rawName)
    {
        using var cn = SqliteConnectionFactory.OpenExisting(path);
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE incident_files SET file_name = $n;";
        cmd.Parameters.AddWithValue("$n", rawName);
        cmd.ExecuteNonQuery();
    }

    private async Task<byte[]?> ReadAsTheSessionDoes(string path)
    {
        var loaded = Assert.Single(IncidentRepository.Load(path).Files);
        return await _store.TryReadBytesAsync(path, IncidentFile.StorageFileName(loaded.Id, loaded.FileName));
    }

    [Fact]
    public async Task A_file_name_with_an_embedded_nul_does_not_stop_the_incident_from_opening()
    {
        var bytes = new byte[] { 3, 1, 4 };
        var (path, _) = SaveWithLegacyFile(".lagebuch", bytes);
        SetRawFileName(path, "foto.jp\0g");

        var incident = IncidentRepository.Load(path);

        Assert.Equal("foto.jpg", Assert.Single(incident.Files).FileName);
        Assert.Equal(bytes, await ReadAsTheSessionDoes(path));
        Assert.False(Directory.Exists(FolderFor(path)));
    }

    [Fact]
    public async Task A_raw_extension_the_sanitiser_changes_is_imported_under_the_key_the_reader_uses()
    {
        var bytes = new byte[] { 2, 7, 1, 8 };
        var (path, _) = SaveWithLegacyFile(".lagebuch", bytes);
        SetRawFileName(path, "foto.jp<g");

        Assert.Equal(bytes, await ReadAsTheSessionDoes(path));
        Assert.False(Directory.Exists(FolderFor(path)));
    }

    [Fact]
    public void A_source_failing_mid_copy_with_any_exception_leaves_no_row_behind()
    {
        var (path, file) = SaveWithLegacyFile(".lagebuch", new byte[] { 1, 2, 3, 4, 5, 6 });
        var legacy = Path.Join(FolderFor(path), IncidentFile.StorageFileName(file.Id, file.FileName));

        using (var cn = SqliteConnectionFactory.OpenExisting(path))
        {
            LegacyAttachmentFolder.Import(cn, path, openSource: _ => new FailingAfterFirstReadStream(6));
        }

        Assert.Equal(0, BlobRowCount(path));
        Assert.True(File.Exists(legacy));
    }

    private sealed class FailingAfterFirstReadStream(long length) : Stream
    {
        private bool _readOnce;

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => length;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_readOnce)
            {
                throw new UnauthorizedAccessException("revoked mid-copy");
            }

            _readOnce = true;
            buffer[offset] = 1;
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset Now { get; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.Zero);
    }
}
