namespace LageBuch.Persistence.Tests;

public class IncidentFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"filestore-{Guid.NewGuid():N}");
    private readonly string _incidentPath;
    private readonly IncidentFileStore _store = new();

    public IncidentFileStoreTests() =>
        _incidentPath = Path.Combine(_dir, "20260622-0900-B.fwincident");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static async Task<byte[]?> TryReadAsync(IncidentFileStore store, string incidentPath, string storageFileName)
    {
        var path = store.ResolveDiskPath(incidentPath, storageFileName);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    [Fact]
    public async Task SaveStreamAsync_then_read_back_round_trips()
    {
        Directory.CreateDirectory(_dir);
        var bytes = new byte[] { 1, 2, 3, 4 };

        await _store.SaveStreamAsync(_incidentPath, "abc.jpg", new MemoryStream(bytes));
        var read = await TryReadAsync(_store, _incidentPath, "abc.jpg");

        Assert.Equal(bytes, read);
    }

    [Fact]
    public async Task SaveStreamAsync_overwrites_an_existing_file_at_the_same_storage_name()
    {
        // Idempotency matters once uploads can be retried (issue #167 P1 #2) — a repeat write for
        // the same storage name must replace, not corrupt or duplicate, the on-disk content.
        Directory.CreateDirectory(_dir);
        await _store.SaveStreamAsync(_incidentPath, "abc.jpg", new MemoryStream(new byte[] { 1, 2, 3 }));

        await _store.SaveStreamAsync(_incidentPath, "abc.jpg", new MemoryStream(new byte[] { 9, 9 }));
        var read = await TryReadAsync(_store, _incidentPath, "abc.jpg");

        Assert.Equal(new byte[] { 9, 9 }, read);
    }

    [Fact]
    public async Task SaveStreamAsync_creates_a_sibling_files_folder_next_to_the_incident()
    {
        Directory.CreateDirectory(_dir);
        await _store.SaveStreamAsync(_incidentPath, "abc.jpg", new MemoryStream(new byte[] { 1 }));

        var expectedFolder = Path.Combine(_dir, "20260622-0900-B.files");
        Assert.True(Directory.Exists(expectedFolder));
        Assert.True(File.Exists(Path.Combine(expectedFolder, "abc.jpg")));
    }

    [Fact]
    public void ResolveDiskPath_of_a_file_that_was_never_written_does_not_exist()
    {
        Assert.False(File.Exists(_store.ResolveDiskPath(_incidentPath, "missing.jpg")));
    }

    [Fact]
    public void ResolveDiskPath_does_not_throw_when_the_folder_does_not_exist()
    {
        // _dir is deliberately never created here — ResolveDiskPath is a pure path computation and
        // never touches disk, so a caller (like GetFileStreamAsync) checks File.Exists itself.
        Assert.False(File.Exists(_store.ResolveDiskPath(_incidentPath, "abc.jpg")));
    }
}
