using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class JsonRecentFilesStoreTests : IDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"recent-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
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
        {
            store.Add($"/file{i}.fwincident");
        }

        var recent = store.GetRecent();
        Assert.Equal(10, recent.Count);
        Assert.Equal("/file14.fwincident", recent[0]);
    }

    [Fact]
    public void Remove_drops_the_entry_and_keeps_the_rest_in_order()
    {
        var store = new JsonRecentFilesStore(_path);
        store.Add("/a.fwincident");
        store.Add("/b.fwincident");
        store.Add("/c.fwincident");

        store.Remove("/b.fwincident");

        var recent = new JsonRecentFilesStore(_path).GetRecent();
        Assert.Equal(new[] { "/c.fwincident", "/a.fwincident" }, recent);
    }

    // Add already treats paths differing only in case as one entry; Remove must find that entry
    // the same way, or a row could be listed that no button can take off.
    [Fact]
    public void Remove_matches_the_path_case_insensitively_like_add()
    {
        var store = new JsonRecentFilesStore(_path);
        store.Add("/Einsatz.fwincident");

        store.Remove("/einsatz.fwincident");

        Assert.Empty(new JsonRecentFilesStore(_path).GetRecent());
    }

    [Fact]
    public void Removing_an_unlisted_path_leaves_the_list_unchanged()
    {
        var store = new JsonRecentFilesStore(_path);
        store.Add("/a.fwincident");

        store.Remove("/b.fwincident");

        Assert.Equal(new[] { "/a.fwincident" }, new JsonRecentFilesStore(_path).GetRecent());
    }

    [Fact]
    public void Removing_from_a_missing_file_creates_none()
    {
        new JsonRecentFilesStore(_path).Remove("/a.fwincident");

        Assert.False(File.Exists(_path));
    }
}
