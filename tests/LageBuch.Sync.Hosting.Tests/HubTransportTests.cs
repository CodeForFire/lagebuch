using static LageBuch.Sync.Hosting.Tests.SnapshotFixture;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The push channel runs on Server-Sent Events and nothing else (#781). SignalR's default
/// negotiation tried WebSockets first; the socket checked the host's self-signed certificate against
/// the system store, failed with UntrustedRoot, and the client negotiated a second time before
/// settling on SSE — on every connect and every reconnect. The hub only pushes snapshots, which is
/// exactly what an SSE stream carries, so the client now asks for that transport directly.
/// </summary>
public class HubTransportTests
{
    [Fact]
    public async Task A_joined_client_negotiates_once_and_listens_over_server_sent_events()
    {
        await using var host = await ScriptedSnapshotHost.StartAsync(BaseSnapshot());
        await using var client = await ConnectAsync(host);

        // A push that arrives proves the stream is up, not merely requested.
        await host.PushAsync(Revised(host.Current, 1, "Lage erkundet"));
        await WaitForClient(client, () => JournalOf(client).Contains("Lage erkundet"), "the pushed snapshot");

        var requests = host.HubRequests;
        Assert.Single(requests, r => r.IsNegotiate);
        Assert.Contains(requests, r => r.IsServerSentEvents);
        Assert.DoesNotContain(requests, r => r.IsWebSocket);
    }

    // Long polling is not a fallback either: it was never exercised, and the reconcile poll already
    // keeps a device converging when no push arrives. A network that breaks SSE fails the join, where
    // the Lagebuchführer sees it, instead of quietly switching to a third transport.
    [Fact]
    public async Task A_host_that_refuses_server_sent_events_fails_the_join_rather_than_long_polling()
    {
        await using var host = await ScriptedSnapshotHost.StartAsync(BaseSnapshot());
        host.RefuseServerSentEvents = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(host));

        Assert.DoesNotContain(host.HubRequests, r => !r.IsNegotiate && !r.IsServerSentEvents);
    }
}
