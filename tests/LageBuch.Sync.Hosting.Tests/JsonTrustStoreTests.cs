namespace LageBuch.Sync.Hosting.Tests;

public class JsonTrustStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trust-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Empty_or_missing_file_returns_null()
    {
        var store = new JsonTrustStore(_path);
        Assert.Null(store.GetThumbprint("192.168.0.5"));
    }

    [Fact]
    public void Save_then_get_round_trips()
    {
        var store = new JsonTrustStore(_path);
        store.SaveThumbprint("192.168.0.5", "AB12CD34");
        Assert.Equal("AB12CD34", new JsonTrustStore(_path).GetThumbprint("192.168.0.5"));
    }

    [Fact]
    public void Corrupt_file_returns_null_and_does_not_crash()
    {
        File.WriteAllText(_path, "{ not json");
        var store = new JsonTrustStore(_path);
        Assert.Null(store.GetThumbprint("192.168.0.5"));
    }

    [Fact]
    public void Hosts_are_keyed_independently()
    {
        var store = new JsonTrustStore(_path);
        store.SaveThumbprint("10.0.0.1", "AAAA");
        store.SaveThumbprint("10.0.0.2", "BBBB");
        Assert.Equal("AAAA", store.GetThumbprint("10.0.0.1"));
        Assert.Equal("BBBB", store.GetThumbprint("10.0.0.2"));
    }

    [Fact]
    public void CertificateChangedException_carries_the_host_and_a_german_message()
    {
        var ex = new CertificateChangedException("10.0.0.5");
        Assert.Contains("10.0.0.5", ex.Message, StringComparison.Ordinal);
        Assert.True(ex.Message.Contains("geändert", StringComparison.Ordinal));
    }

    [Fact]
    public void Remove_clears_a_saved_thumbprint()
    {
        var store = new JsonTrustStore(_path);
        store.SaveThumbprint("10.0.0.5", "AAAA");
        store.RemoveThumbprint("10.0.0.5");
        Assert.Null(new JsonTrustStore(_path).GetThumbprint("10.0.0.5"));
    }

    [Fact]
    public void Remove_of_an_unknown_host_does_not_throw()
    {
        var store = new JsonTrustStore(_path);
        store.RemoveThumbprint("10.0.0.5"); // never saved -- must be a no-op, not an error
        Assert.Null(store.GetThumbprint("10.0.0.5"));
    }
}
