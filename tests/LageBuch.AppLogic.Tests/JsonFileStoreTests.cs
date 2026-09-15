using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class JsonFileStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"json-file-store-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        foreach (var path in new[] { _path, _path + ".tmp" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Missing_file_reads_as_no_value()
    {
        Assert.False(new JsonFileStore<string>(_path).TryRead(out _));
    }

    [Fact]
    public void Corrupt_json_reads_as_no_value()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.False(new JsonFileStore<string>(_path).TryRead(out _));
    }

    [Fact]
    public void A_stored_json_null_reads_as_no_value()
    {
        File.WriteAllText(_path, "null");

        Assert.False(new JsonFileStore<string>(_path).TryRead(out _));
    }

    [Fact]
    public void A_written_value_reads_back()
    {
        new JsonFileStore<string>(_path).Write("/einsatz/2026-09-15.fwincident");

        Assert.True(new JsonFileStore<string>(_path).TryRead(out var value));
        Assert.Equal("/einsatz/2026-09-15.fwincident", value);
    }

    [Fact]
    public void A_value_type_payload_round_trips_and_stays_distinguishable_from_no_value()
    {
        // LastPdfExport is a record struct, so a `T?` return would hand back a default-valued
        // record for a missing file rather than "nothing stored" -- the reason TryRead exists.
        var exportedAt = new DateTimeOffset(2026, 9, 15, 8, 30, 0, TimeSpan.FromHours(2));
        new JsonFileStore<LastPdfExport>(_path).Write(new LastPdfExport("/export.pdf", exportedAt));

        Assert.True(new JsonFileStore<LastPdfExport>(_path).TryRead(out var stored));
        Assert.Equal("/export.pdf", stored.Path);
        Assert.Equal(exportedAt, stored.ExportedAt);

        File.Delete(_path);
        Assert.False(new JsonFileStore<LastPdfExport>(_path).TryRead(out _));
    }

    [Fact]
    public void Write_leaves_no_tmp_sibling_behind()
    {
        new JsonFileStore<string>(_path).Write("kein-rest");

        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Write_overwrites_a_stale_leftover_tmp_file_from_a_previous_crash()
    {
        // Simulates a crash mid-write in an earlier process: a *.tmp sibling left over from a write
        // that never reached File.Move. The next write must clobber it, not fail or leave it behind.
        File.WriteAllText(_path + ".tmp", "garbage-from-a-crashed-write");

        new JsonFileStore<string>(_path).Write("frisch");

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.True(new JsonFileStore<string>(_path).TryRead(out var value));
        Assert.Equal("frisch", value);
    }

    [Fact]
    public void A_failed_write_leaves_the_previous_file_intact()
    {
        new JsonFileStore<string>(_path).Write("bewaehrt");

        // A directory sitting where the temp file wants to go fails the write before File.Move runs.
        Directory.CreateDirectory(_path + ".tmp");
        try
        {
            // SystemException, not IOException: opening a path that is a directory surfaces as
            // UnauthorizedAccessException on Linux and IOException on Windows.
            var store = new JsonFileStore<string>(_path);
            Assert.ThrowsAny<SystemException>(() => store.Write("verloren"));

            Assert.True(new JsonFileStore<string>(_path).TryRead(out var value));
            Assert.Equal("bewaehrt", value);
        }
        finally
        {
            Directory.Delete(_path + ".tmp");
        }
    }
}
