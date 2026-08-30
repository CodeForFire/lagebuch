using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

public class RemoteClientTests
{
    private static LocalIncidentSession HostSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            new[] { ("Punkt A", false) },
            Array.Empty<(string, bool)>());

    // Completes when the session next raises Changed (times out so a broken broadcast fails fast).
    private static Task NextChange(RemoteIncidentSession session, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource();
        void Handler() => tcs.TrySetResult();
        session.Changed += Handler;
        return tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5))
            .ContinueWith(
                t =>
                {
                    session.Changed -= Handler;
                    t.GetAwaiter().GetResult();
                },
                TaskContinuationOptions.ExecuteSynchronously);
    }

    [Fact]
    public async Task Client_sees_its_own_command_reflected_via_the_host_broadcast()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            TestHost.DefaultPin,
            port);

        var change = NextChange(client);
        await client.SendAsync(new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"),
            EtbDirection.Incoming,
            "Von der Einsatzstelle",
            "Leitstelle",
            "ELW"));
        await change;

        var entry = Assert.Single(client.Incident.Journal, e => e.Text == "Von der Einsatzstelle");
        Assert.Equal("Client (RUF 1)", entry.EnteredBy); // attributed to this device, not the host
    }

    [Fact]
    public async Task Two_clients_converge_on_the_hosts_state()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        await using var a = await RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("A"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);
        await using var b = await RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("B"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);

        var aChange = NextChange(a);
        var bChange = NextChange(b);
        await a.SendAsync(new AddForceUnitCommand(new OperatorDto("A", null), "Aich", 9, "Aich 42/1", "Im Einsatz", null, 4));
        await Task.WhenAll(aChange, bChange);

        Assert.Equal(9, a.Incident.TotalPersonnel);
        Assert.Equal(9, b.Incident.TotalPersonnel); // the other device sees A's change
    }

    [Fact]
    public async Task AddFileAsync_uploads_and_the_other_client_sees_metadata_and_can_pull_bytes()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        await using var uploader = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("A", "RUF 1"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);
        await using var observer = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("B"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);

        var uploaderChange = NextChange(uploader);
        var observerChange = NextChange(observer);
        var bytes = new byte[] { 9, 8, 7, 6 };
        await uploader.AddFileAsync("brand.jpg", "image/jpeg", bytes);
        await Task.WhenAll(uploaderChange, observerChange);

        var uploaderFile = Assert.Single(uploader.Incident.Files);
        var observerFile = Assert.Single(observer.Incident.Files);
        Assert.Equal(uploaderFile.Id, observerFile.Id);
        Assert.Equal("A (RUF 1)", observerFile.AddedBy);

        // The metadata above arrived via the ordinary broadcast; bytes are a separate, on-demand pull.
        Assert.Equal(bytes, await observer.GetFileBytesAsync(observerFile.Id));
    }

    [Fact]
    public async Task RenameFile_syncs_to_other_joined_clients()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        await using var renamer = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("A", "RUF 1"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);
        await using var observer = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("B"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);

        var renamerAdded = NextChange(renamer);
        var observerAdded = NextChange(observer);
        await renamer.AddFileAsync("brand.jpg", "image/jpeg", new byte[] { 1 });
        await Task.WhenAll(renamerAdded, observerAdded);
        var fileId = Assert.Single(renamer.Incident.Files).Id;

        var renamerRenamed = NextChange(renamer);
        var observerRenamed = NextChange(observer);
        renamer.RenameFile(fileId, "Küchenbrand");
        await Task.WhenAll(renamerRenamed, observerRenamed);

        Assert.Equal("Küchenbrand", Assert.Single(renamer.Incident.Files).DisplayName);
        Assert.Equal("Küchenbrand", Assert.Single(observer.Incident.Files).DisplayName);
    }

    [Fact]
    public async Task GetFileBytesAsync_returns_null_for_an_unknown_file()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;
        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);

        Assert.Null(await client.GetFileBytesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddFileAsync_rejects_a_file_over_the_size_cap_before_ever_sending_it()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;
        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);

        var tooBig = new byte[LageBuch.Domain.Files.IncidentFile.MaxSizeBytes + 1];
        await Assert.ThrowsAsync<ArgumentException>(() => client.AddFileAsync("huge.pdf", "application/pdf", tooBig));
        Assert.Empty(client.Incident.Files);
    }

    [Fact]
    public async Task GetFileBytesAsync_caches_pulled_bytes_to_disk_when_a_cache_root_is_supplied()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"attachment-cache-{Guid.NewGuid():N}");
        try
        {
            await using var uploader = await RemoteIncidentSession.ConnectAsync(
                "127.0.0.1", new SessionOperator("A"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port);
            var bytes = new byte[] { 1, 2, 3 };
            var uploaderChange = NextChange(uploader);
            await uploader.AddFileAsync("brand.jpg", "image/jpeg", bytes);
            await uploaderChange; // AddFileAsync's HTTP response can outrace the self-broadcast
            var fileId = Assert.Single(uploader.Incident.Files).Id;

            await using var puller = await RemoteIncidentSession.ConnectAsync(
                "127.0.0.1",
                new SessionOperator("B"),
                "1.0.0",
                new ImmediateUiDispatcher(),
                TestHost.DefaultPin,
                port,
                cacheRoot: cacheRoot);
            await puller.GetFileBytesAsync(fileId);

            var cached = Directory.EnumerateFiles(cacheRoot, "*", SearchOption.AllDirectories).ToList();
            var cachedFile = Assert.Single(cached);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(cachedFile));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Connect_refuses_a_version_mismatch()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "2.0.0");
        await using var _ = host;

        await Assert.ThrowsAsync<VersionMismatchException>(() =>
            RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("Client"), "1.0.0", new ImmediateUiDispatcher(), TestHost.DefaultPin, port));
    }

    [Fact]
    public async Task Connect_refuses_a_wrong_pin()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        // A wrong PIN is refused at the first request, before the version compare — auth precedes content.
        await Assert.ThrowsAsync<PinRejectedException>(() =>
            RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("Client"), "1.0.0", new ImmediateUiDispatcher(), "9999", port));
    }

    [Fact]
    public async Task Connect_refuses_a_missing_pin()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0", pin: "1234");
        await using var _ = host;

        await Assert.ThrowsAsync<PinRejectedException>(() =>
            RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("Client"), "1.0.0", new ImmediateUiDispatcher(), pin: null, port));
    }
}
