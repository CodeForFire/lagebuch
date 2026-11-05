using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Map;

namespace LageBuch.Sync.Hosting.Tests;

public class MapOverlayHostTests
{
    private static readonly byte[] Bytes = "{\"type\": \"Point\", \"coordinates\": [11, 48]}"u8.ToArray();

    private static LocalIncidentSession HostSession(FixedClock clock) =>
        TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.lagebuch",
            new[] { ("Punkt A", false) },
            Array.Empty<(string, bool)>());

    private static Task<RemoteIncidentSession> Join(int port, string name) =>
        RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator(name, "RUF 1"), "1.0.0", new ImmediateUiDispatcher(), new InMemoryTrustStore(), TestHost.DefaultPin, port);

    [Fact]
    public async Task An_Ebene_imported_on_a_client_lands_on_the_host_with_its_bytes_and_every_peer_can_pull_them()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;
        await using var importer = await Join(port, "A");
        await using var observer = await Join(port, "B");
        var observed = new TaskCompletionSource();
        observer.Changed += () =>
        {
            if (observer.Incident.MapOverlays.Count > 0)
            {
                observed.TrySetResult();
            }
        };

        await importer.AddMapOverlayAsync("messpunkt.geojson", "application/geo+json", Bytes, new MapOverlaySummary("messpunkt", 1, null, null));
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var overlay = Assert.Single(hostSession.Incident.MapOverlays);
        Assert.Equal(Bytes, await hostSession.GetFileBytesAsync(overlay.FileId));
        Assert.Equal(Bytes, await observer.GetFileBytesAsync(overlay.FileId));
    }

    [Fact]
    public async Task The_host_announces_an_Ebene_s_upload_so_every_device_looks_for_the_file_again()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;
        await using var importer = await Join(port, "A");
        var changes = 0;
        hostSession.Changed += () => changes++;

        await importer.AddMapOverlayAsync("messpunkt.geojson", "application/geo+json", Bytes, new MapOverlaySummary("messpunkt", 1, null, null));

        // Once for the command, once more when the bytes it named arrived.
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task A_plain_attachment_s_upload_is_not_announced_again()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;
        await using var uploader = await Join(port, "A");
        var changes = 0;
        hostSession.Changed += () => changes++;

        await uploader.AddFileAsync("brand.jpg", "image/jpeg", [1, 2, 3]);

        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Removing_an_Ebene_on_a_client_wipes_its_bytes_on_the_host()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;
        await using var client = await Join(port, "A");
        await client.AddMapOverlayAsync("messpunkt.geojson", "application/geo+json", Bytes, new MapOverlaySummary("messpunkt", 1, null, null));
        var overlay = Assert.Single(hostSession.Incident.MapOverlays);

        await client.RemoveMapOverlayAsync(overlay.Id);

        Assert.Empty(hostSession.Incident.MapOverlays);
        Assert.Empty(hostSession.Incident.Files);
        Assert.Null(await hostSession.GetFileBytesAsync(overlay.FileId));
    }

    [Fact]
    public async Task A_client_cannot_slip_an_archive_in_as_a_plain_attachment()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock, "1.0.0");
        await using var _ = host;
        await using var attacker = await Join(port, "A");

        await Assert.ThrowsAsync<CommandRejectedException>(() =>
            attacker.SendAsync(new AddFileCommand(new OperatorDto("A", "RUF 1"), Guid.NewGuid(), "werkzeug.zip", "application/zip", 4)));

        Assert.Empty(hostSession.Incident.Files);
    }
}
