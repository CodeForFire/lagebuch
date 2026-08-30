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
}
