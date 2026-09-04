using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class JsonLastSaveFolderStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"last-folder-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        var store = new JsonLastSaveFolderStore(_path);
        Assert.Null(store.GetLastFolder());
    }

    [Fact]
    public void SetLastFolder_persists_and_overwrites()
    {
        new JsonLastSaveFolderStore(_path).SetLastFolder("/einsaetze/2026");
        Assert.Equal("/einsaetze/2026", new JsonLastSaveFolderStore(_path).GetLastFolder());

        new JsonLastSaveFolderStore(_path).SetLastFolder("/einsaetze/2027");
        Assert.Equal("/einsaetze/2027", new JsonLastSaveFolderStore(_path).GetLastFolder());
    }

    [Fact]
    public void GetLastFolder_after_the_first_call_does_not_re_read_the_file()
    {
        new JsonLastSaveFolderStore(_path).SetLastFolder("/einsaetze/2026");
        var reader = new JsonLastSaveFolderStore(_path);
        var first = reader.GetLastFolder(); // warms the in-memory cache from disk

        File.Delete(_path);

        Assert.Equal(first, reader.GetLastFolder());
    }
}
