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

    [Fact]
    public void The_pin_round_trips()
    {
        new JsonLastConnectionStore(_path).SetLast(new LastConnection("elw-1", null, T0, "5393"));

        Assert.Equal("5393", new JsonLastConnectionStore(_path).GetLast()?.Pin);
    }

    [Fact]
    public void A_record_written_before_the_pin_was_kept_reads_without_one()
    {
        File.WriteAllText(_path, """{"Host":"elw-1","Keyword":"B3 Wohnung","ConnectedAt":"2026-09-24T14:05:00+02:00"}""");

        Assert.Equal(new LastConnection("elw-1", "B3 Wohnung", T0), new JsonLastConnectionStore(_path).GetLast());
    }

    [Fact]
    public void Clear_removes_the_file_pin_and_all()
    {
        var store = new JsonLastConnectionStore(_path);
        store.SetLast(new LastConnection("elw-1", null, T0, "5393"));

        store.Clear();

        Assert.False(File.Exists(_path));
        Assert.Null(store.GetLast());
    }

    [Fact]
    public void Clear_without_a_stored_connection_does_nothing()
    {
        new JsonLastConnectionStore(_path).Clear();

        Assert.False(File.Exists(_path));
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
