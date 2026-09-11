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

    // §review-trust-store: GetThumbprint used to read the plain Dictionary without the _gate lock
    // that Save/RemoveThumbprint take. It's called from the TLS ServerCertificateCustomValidationCallback,
    // which can run concurrently on multiple HTTP/SignalR handshake threads while a Save/Remove is in
    // flight on another -- a torn read on Dictionary<TKey,TValue> is undefined behavior (an exception,
    // a hang while it resizes, or silently wrong data), not just a benign race. This mixes all three
    // operations across many hosts under Parallel.For.
    //
    // Without the _gate lock in GetThumbprint this did NOT fail deterministically here: hammering the
    // unlocked read against concurrent Save/Remove with up to 200,000 iterations / 32-way parallelism
    // / as few as 4 shared hosts (to maximize contention on the same dictionary buckets) still came
    // back green every time in this environment -- .NET's Dictionary<TKey,TValue> read/write race is
    // real (documented as unsupported/undefined) but not reliably reproducible as a test failure on a
    // given CLR/hardware combination. Kept at a lighter weight as a regression guard: it must never
    // throw, and afterwards a fresh Load() from disk must agree with the live instance for every host.
    [Fact]
    public void Concurrent_get_save_and_remove_across_many_hosts_do_not_throw_and_end_consistent()
    {
        var store = new JsonTrustStore(_path);
        var hosts = Enumerable.Range(0, 24).Select(i => $"10.0.0.{i}").ToArray();

        Parallel.For(0, 3000, i =>
        {
            var host = hosts[i % hosts.Length];
            switch (i % 3)
            {
                case 0:
                    store.SaveThumbprint(host, $"THUMB-{i:D6}");
                    break;
                case 1:
                    store.GetThumbprint(host);
                    break;
                default:
                    store.RemoveThumbprint(host);
                    break;
            }
        });

        // No torn write: the file on disk parses and agrees with the live instance for every host.
        var reloaded = new JsonTrustStore(_path);
        foreach (var host in hosts)
        {
            Assert.Equal(store.GetThumbprint(host), reloaded.GetThumbprint(host));
        }

        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void SaveThumbprint_writes_atomically_and_leaves_no_tmp_sibling()
    {
        var store = new JsonTrustStore(_path);
        store.SaveThumbprint("10.0.0.9", "ABCD1234");

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Equal("ABCD1234", new JsonTrustStore(_path).GetThumbprint("10.0.0.9"));
    }

    [Fact]
    public void RemoveThumbprint_writes_atomically_and_leaves_no_tmp_sibling()
    {
        var store = new JsonTrustStore(_path);
        store.SaveThumbprint("10.0.0.9", "ABCD1234");
        store.RemoveThumbprint("10.0.0.9");

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Null(new JsonTrustStore(_path).GetThumbprint("10.0.0.9"));
    }

    [Fact]
    public void SaveThumbprint_overwrites_a_stale_leftover_tmp_file_from_a_previous_crash()
    {
        // Simulates a crash mid-write in an earlier process: a *.tmp sibling left over from a write
        // that never reached File.Move. The next write must clobber it, not fail or leave it behind.
        File.WriteAllText(_path + ".tmp", "garbage-from-a-crashed-write");

        var store = new JsonTrustStore(_path);
        store.SaveThumbprint("10.0.0.9", "ABCD1234");

        Assert.False(File.Exists(_path + ".tmp"));
        Assert.Equal("ABCD1234", new JsonTrustStore(_path).GetThumbprint("10.0.0.9"));
    }
}
