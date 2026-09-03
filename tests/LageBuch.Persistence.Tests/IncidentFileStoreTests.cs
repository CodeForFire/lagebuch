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

    [Fact]
    public async Task SaveBytesAsync_then_TryReadBytesAsync_round_trips()
    {
        Directory.CreateDirectory(_dir);
        var bytes = new byte[] { 1, 2, 3, 4 };

        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", bytes);
        var read = await _store.TryReadBytesAsync(_incidentPath, "abc.jpg");

        Assert.Equal(bytes, read);
    }

    [Fact]
    public async Task SaveBytesAsync_overwrites_an_existing_file_at_the_same_storage_name()
    {
        // Idempotency matters once uploads can be retried (issue #167 P1 #2) — a repeat write for
        // the same storage name must replace, not corrupt or duplicate, the on-disk content.
        Directory.CreateDirectory(_dir);
        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 1, 2, 3 });

        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 9, 9 });
        var read = await _store.TryReadBytesAsync(_incidentPath, "abc.jpg");

        Assert.Equal(new byte[] { 9, 9 }, read);
    }

    [Fact]
    public async Task SaveBytesAsync_creates_a_sibling_files_folder_next_to_the_incident()
    {
        Directory.CreateDirectory(_dir);
        await _store.SaveBytesAsync(_incidentPath, "abc.jpg", new byte[] { 1 });

        var expectedFolder = Path.Combine(_dir, "20260622-0900-B.files");
        Assert.True(Directory.Exists(expectedFolder));
        Assert.True(File.Exists(Path.Combine(expectedFolder, "abc.jpg")));
    }

    [Fact]
    public async Task TryReadBytesAsync_returns_null_for_a_file_that_was_never_written()
    {
        Assert.Null(await _store.TryReadBytesAsync(_incidentPath, "missing.jpg"));
    }

    [Fact]
    public async Task TryReadBytesAsync_returns_null_rather_than_throwing_when_the_folder_does_not_exist()
    {
        // _dir is deliberately never created here — degrades quietly, like TryReadState.
        Assert.Null(await _store.TryReadBytesAsync(_incidentPath, "abc.jpg"));
    }
}
