using Feuerwehr.AppLogic.Services;

namespace Feuerwehr.AppLogic.Tests;

public class JsonRecentFilesStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"recent-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Missing_file_returns_empty()
    {
        var store = new JsonRecentFilesStore(_path);
        Assert.Empty(store.GetRecent());
    }

    [Fact]
    public void Add_puts_most_recent_first_and_persists()
    {
        new JsonRecentFilesStore(_path).Add("/a.fwincident");
        new JsonRecentFilesStore(_path).Add("/b.fwincident");

        var recent = new JsonRecentFilesStore(_path).GetRecent();
        Assert.Equal(new[] { "/b.fwincident", "/a.fwincident" }, recent);
    }

    [Fact]
    public void Add_existing_path_moves_it_to_front_without_duplicating()
    {
        var store = new JsonRecentFilesStore(_path);
        store.Add("/a.fwincident");
        store.Add("/b.fwincident");
        store.Add("/a.fwincident");

        var recent = new JsonRecentFilesStore(_path).GetRecent();
        Assert.Equal(new[] { "/a.fwincident", "/b.fwincident" }, recent);
    }

    [Fact]
    public void List_is_capped_at_ten()
    {
        var store = new JsonRecentFilesStore(_path);
        for (var i = 0; i < 15; i++)
            store.Add($"/file{i}.fwincident");

        var recent = store.GetRecent();
        Assert.Equal(10, recent.Count);
        Assert.Equal("/file14.fwincident", recent[0]);
    }
}
