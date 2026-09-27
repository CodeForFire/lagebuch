using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

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
}
