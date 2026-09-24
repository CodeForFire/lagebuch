using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

public class JsonLastConnectionStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 14, 5, 0, TimeSpan.FromHours(2));

    private readonly string _path = Path.Join(Path.GetTempPath(), $"last-connection-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        Assert.Null(new JsonLastConnectionStore(_path).GetLast());
    }

    [Fact]
    public void SetLast_persists_and_overwrites()
    {
        new JsonLastConnectionStore(_path).SetLast(new LastConnection("elw-1", "B3 Wohnung", T0));
        Assert.Equal(new LastConnection("elw-1", "B3 Wohnung", T0), new JsonLastConnectionStore(_path).GetLast());

        new JsonLastConnectionStore(_path).SetLast(new LastConnection("192.168.1.10:5859", null, T0.AddHours(1)));
        Assert.Equal(new LastConnection("192.168.1.10:5859", null, T0.AddHours(1)), new JsonLastConnectionStore(_path).GetLast());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"elw-1\"")] // the shape of the last-join-host.json this store replaced
    public void A_record_without_a_host_returns_null(string json)
    {
        File.WriteAllText(_path, json);

        Assert.Null(new JsonLastConnectionStore(_path).GetLast());
    }
}
