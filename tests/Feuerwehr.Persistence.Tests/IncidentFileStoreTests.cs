namespace Feuerwehr.Persistence.Tests;

public class IncidentFileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"filestore-{Guid.NewGuid():N}");
    private readonly string _incidentPath;
    private readonly IncidentFileStore _store = new();

    public IncidentFileStoreTests() =>
        _incidentPath = Path.Combine(_dir, "20260622-0900-B.fwincident");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveBytes_then_TryReadBytes_round_trips()
    {
        Directory.CreateDirectory(_dir);
        var bytes = new byte[] { 1, 2, 3, 4 };

        _store.SaveBytes(_incidentPath, "abc.jpg", bytes);
        var read = _store.TryReadBytes(_incidentPath, "abc.jpg");

        Assert.Equal(bytes, read);
    }

    [Fact]
    public void SaveBytes_creates_a_sibling_files_folder_next_to_the_incident()
    {
        Directory.CreateDirectory(_dir);
        _store.SaveBytes(_incidentPath, "abc.jpg", new byte[] { 1 });

        var expectedFolder = Path.Combine(_dir, "20260622-0900-B.files");
        Assert.True(Directory.Exists(expectedFolder));
        Assert.True(File.Exists(Path.Combine(expectedFolder, "abc.jpg")));
    }

    [Fact]
    public void TryReadBytes_returns_null_for_a_file_that_was_never_written()
    {
        Assert.Null(_store.TryReadBytes(_incidentPath, "missing.jpg"));
    }

    [Fact]
    public void TryReadBytes_returns_null_rather_than_throwing_when_the_folder_does_not_exist()
    {
        // _dir is deliberately never created here — degrades quietly, like TryReadState.
        Assert.Null(_store.TryReadBytes(_incidentPath, "abc.jpg"));
    }
}
