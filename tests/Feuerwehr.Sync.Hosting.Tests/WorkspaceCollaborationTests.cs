using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.AppLogic.ViewModels;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;
using Microsoft.AspNetCore.SignalR.Client;

namespace Feuerwehr.Sync.Hosting.Tests;

/// <summary>
/// End-to-end multi-device acceptance: one device hosts an incident over loopback and another joins
/// as a thin client, both driving the real <see cref="IncidentWorkspaceViewModel"/>. Asserts that
/// edits converge in both directions through the VM layer, that the host closing the incident flips
/// the client's workspace read-only, and that losing the host disconnects then returns the client
/// Home (#52 §4/§5/§7).
/// </summary>
public class WorkspaceCollaborationTests
{
    private static LocalIncidentSession HostSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(new InMemoryStore(), clock,
            new SessionOperator("Host", "FFB 1"), "/x.fwincident", new[] { "Punkt A" });

    private static IncidentWorkspaceViewModel Workspace(IIncidentSession session, IClock clock) =>
        new(session, clock, new NoTicker(), MasterDataSet.Empty,
            new NoDialogs(), new NoAlarm(), new NoopIncidentHostController());

    // Completes when the client next applies a host broadcast (times out so a broken push fails fast).
    private static Task NextChange(RemoteIncidentSession session, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource();
        void Handler() => tcs.TrySetResult();
        session.Changed += Handler;
        return tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5))
            .ContinueWith(t => { session.Changed -= Handler; t.GetAwaiter().GetResult(); },
                TaskContinuationOptions.ExecuteSynchronously);
    }

    [Fact]
    public async Task Client_workspace_reflects_an_edit_made_on_the_host()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);
        await using var _ = host;

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client", "RUF 1"), "1.0.0", port);
        var clientWs = Workspace(client, clock);

        var change = NextChange(client);
        hostSession.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet", "Leitstelle", "ELW");
        await change;

        Assert.Contains(clientWs.Etb.Entries, e => e.Text == "Lage erkundet");
    }

    [Fact]
    public async Task Host_workspace_reflects_an_edit_made_on_a_client()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var hostWs = Workspace(hostSession, clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);
        await using var _ = host;

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client", "RUF 1"), "1.0.0", port);

        var change = NextChange(client);
        client.AddJournalEntry(EtbDirection.Outgoing, "Rückmeldung an ILS", "ELW", "Leitstelle");
        await change; // the client's own command has round-tripped through the host broadcast

        // The host's own UI sees the client's contribution live (not only after a host edit).
        Assert.Contains(hostWs.Etb.Entries, e => e.Text == "Rückmeldung an ILS");
        var entry = Assert.Single(hostSession.Incident.Journal, e => e.Text == "Rückmeldung an ILS");
        Assert.Equal("Client (RUF 1)", entry.EnteredBy); // attributed to the device that typed it (§6)
    }

    [Fact]
    public async Task Host_closing_the_incident_flips_the_client_workspace_read_only()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);
        await using var _ = host;

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client"), "1.0.0", port);
        var clientWs = Workspace(client, clock);
        Assert.False(clientWs.IsReadOnly);

        var change = NextChange(client);
        hostSession.Close();
        await change;

        Assert.True(clientWs.IsReadOnly);
    }

    [Fact]
    public async Task Losing_the_host_disconnects_then_returns_the_client_home()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);

        // Reconnect once quickly then give up, so "host gone" resolves in the test rather than after
        // the production two-minute window — while still exercising the transient-drop banner first.
        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client"), "1.0.0", port, new GiveUpAfterOneRetry());
        var clientWs = Workspace(client, clock);

        var disconnected = new TaskCompletionSource();
        var wentHome = new TaskCompletionSource();
        client.Disconnected += () => disconnected.TrySetResult();
        clientWs.GoHomeRequested = () => wentHome.TrySetResult();

        await host.DisposeAsync(); // the host goes away

        await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(clientWs.IsConnected);   // banner shown, input disabled
        Assert.False(clientWs.IsInputEnabled);

        await wentHome.Task.WaitAsync(TimeSpan.FromSeconds(10)); // reconnect gave up → back to Home
    }

    // One quick retry, then give up. The first (non-null) delay makes SignalR raise Reconnecting
    // (→ Disconnected banner); when that retry fails against the gone host, the next null delay
    // closes the connection (→ Ended → Home). A policy that returns null immediately would skip the
    // Reconnecting event entirely, so we deliberately retry once.
    private sealed class GiveUpAfterOneRetry : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount == 0 ? TimeSpan.FromMilliseconds(50) : null;
    }
}
