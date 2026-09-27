using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The real client against the real <see cref="IncidentHost"/>, with a <see cref="FaultyRelay"/>
/// between them breaking the link the way a network does: resetting it, or letting it go silent.
/// Nothing in production code is hooked for this — TLS, the PIN, the protocol gate and SignalR all run
/// end to end through the relay.
/// </summary>
public class LinkFailureTests
{
    private static LocalIncidentSession HostSession(FixedClock clock) => TestSession.StartNew(
        new InMemoryStore(),
        clock,
        new SessionOperator("Host", "FFB 1"),
        "/x.fwincident",
        Array.Empty<(string, bool)>(),
        Array.Empty<(string, bool)>());

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private static Task<RemoteIncidentSession> ConnectAsync(FaultyRelay relay, TimeProvider? time = null) =>
        RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            TestHost.DefaultPin,
            relay.Port,
            new ReconnectImmediately(),
            reconcileInterval: time is null ? TimeSpan.FromMinutes(10) : Interval,
            timeProvider: time);

    private static IncidentWorkspaceViewModel Workspace(IIncidentSession session, FixedClock clock) =>
        new(session, clock, new NoTicker(), MasterDataSet.Empty, new NoDialogs(), new NoAlarm(), new NoopIncidentHostController());

    // Subscribed before whatever is meant to cause it, so the transition cannot slip past.
    private static Task SyncStateBecomes(IncidentWorkspaceViewModel workspace, WorkspaceSyncState state)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IncidentWorkspaceViewModel.SyncState) && workspace.SyncState == state)
            {
                reached.TrySetResult();
            }
        };
        return reached.Task;
    }

    private static async Task Within(Task task, string description)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Timed out waiting for {description}.", ex);
        }
    }

    [Fact]
    public async Task A_dropped_link_reconnects_and_the_client_can_send_again()
    {
        // Regression: SignalR disposes whatever HttpMessageHandlerFactory hands it once a connection
        // ends, and the client handed it the handler its own HttpClient runs on. The first drop
        // therefore disposed both: every reconnect attempt failed with ObjectDisposedException until
        // the policy gave up and the session ended, and every command in the meantime went nowhere.
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);
        await using var _ = host;
        await using var relay = FaultyRelay.Start(port);

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            TestHost.DefaultPin,
            relay.Port,
            new ReconnectImmediately(),
            reconcileInterval: TimeSpan.FromMinutes(10));

        var reconnected = new TaskCompletionSource();
        client.Reconnected += () => reconnected.TrySetResult();

        relay.Reset();
        await Within(reconnected.Task, "the client to reconnect");

        await client.SendAsync(new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Nach dem Abriss", null, null));

        Assert.Contains(hostSession.Incident.Journal, e => e.Text == "Nach dem Abriss");
    }

    [Fact]
    public async Task A_link_gone_silent_leaves_the_stand_unconfirmed_while_signalr_still_believes_it_is_up()
    {
        // The case the reconcile poll exists for: nothing closes, nothing errors, SignalR's own
        // keep-alive has not timed out yet — and the Lage on screen may no longer be the Lage. The
        // footer has to say so within one interval plus the pass's deadline, and recover by itself.
        var clock = new FixedClock();
        var time = new FakeTimeProvider();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);
        await using var _ = host;
        await using var relay = FaultyRelay.Start(port);
        await using var client = await ConnectAsync(relay, time);
        var workspace = Workspace(client, clock);

        Assert.Equal(WorkspaceSyncState.Current, workspace.SyncState);

        var unconfirmed = SyncStateBecomes(workspace, WorkspaceSyncState.Unconfirmed);
        relay.Pause();
        time.Advance(Interval);
        await relay.WaitForHeldTrafficAsync();
        time.Advance(SyncProtocol.ReconcileTimeout);
        await Within(unconfirmed, "the footer to mark the Stand unconfirmed");

        Assert.True(workspace.IsConnected, "SignalR should not have noticed anything yet");

        var current = SyncStateBecomes(workspace, WorkspaceSyncState.Current);
        relay.Resume();
        time.Advance(Interval);
        await Within(current, "the footer to confirm the Stand again");
    }

    [Fact]
    public async Task A_change_made_while_the_link_was_down_reaches_the_client_once_it_is_back()
    {
        // The broadcast for this change goes out while the client has no hub connection at all, so
        // it is lost for good. What brings it over is the catch-up the reconnect runs.
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        var (host, port) = await TestHost.StartAsync(hostSession, clock);
        await using var _ = host;
        await using var relay = FaultyRelay.Start(port);
        await using var client = await ConnectAsync(relay);

        var disconnected = new TaskCompletionSource();
        client.Disconnected += () => disconnected.TrySetResult();

        // Paused first, so the reconnect attempts that follow the reset are held rather than
        // completing before the change below is made.
        relay.Pause();
        relay.Reset();
        await Within(disconnected.Task, "the client to notice the reset");

        hostSession.AddJournalEntry(EtbDirection.Outgoing, "Während der Trennung");
        var caughtUp = SnapshotFixture.WaitForClient(
            client,
            () => client.Incident.Journal.Any(e => e.Text == "Während der Trennung"),
            "the change made during the outage",
            TimeSpan.FromSeconds(10));

        relay.Resume();
        await caughtUp;
    }
}
