using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class JsonLastJoinHostStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"last-join-host-{Guid.NewGuid():N}.json");

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
        var store = new JsonLastJoinHostStore(_path);
        Assert.Null(store.GetLastHost());
    }

    [Fact]
    public void SetLastHost_persists_and_overwrites()
    {
        new JsonLastJoinHostStore(_path).SetLastHost("elw-1");
        Assert.Equal("elw-1", new JsonLastJoinHostStore(_path).GetLastHost());

        new JsonLastJoinHostStore(_path).SetLastHost("192.168.1.10:5859");
        Assert.Equal("192.168.1.10:5859", new JsonLastJoinHostStore(_path).GetLastHost());
    }
}
