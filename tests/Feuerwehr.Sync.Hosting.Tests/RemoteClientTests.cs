using Feuerwehr.AppLogic;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Sync.Hosting.Tests;

public class RemoteClientTests
{
    private static LocalIncidentSession HostSession(FixedClock clock) =>
        LocalIncidentSession.StartNew(new InMemoryStore(), clock,
            new SessionOperator("Host", "FFB 1"), "/x.fwincident", new[] { "Punkt A" });

    // Completes when the session next raises Changed (times out so a broken broadcast fails fast).
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
    public async Task Client_sees_its_own_command_reflected_via_the_host_broadcast()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "1.0.0");
        await using var _ = host;

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1", new SessionOperator("Client", "RUF 1"), "1.0.0", port);

        var change = NextChange(client);
        await client.SendAsync(new AddJournalEntryCommand(new OperatorDto("Client", "RUF 1"),
            EtbDirection.Incoming, "Von der Einsatzstelle", "Leitstelle", "ELW"));
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

        await using var a = await RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("A"), "1.0.0", port);
        await using var b = await RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("B"), "1.0.0", port);

        var aChange = NextChange(a);
        var bChange = NextChange(b);
        await a.SendAsync(new AddForceUnitCommand(new OperatorDto("A", null), "Aich", 9, "Aich 42/1", "Im Einsatz", null, 4));
        await Task.WhenAll(aChange, bChange);

        Assert.Equal(9, a.Incident.TotalPersonnel);
        Assert.Equal(9, b.Incident.TotalPersonnel); // the other device sees A's change
    }

    [Fact]
    public async Task Connect_refuses_a_version_mismatch()
    {
        var clock = new FixedClock();
        var (host, port) = await TestHost.StartAsync(HostSession(clock), clock, "2.0.0");
        await using var _ = host;

        await Assert.ThrowsAsync<VersionMismatchException>(() =>
            RemoteIncidentSession.ConnectAsync("127.0.0.1", new SessionOperator("Client"), "1.0.0", port));
    }
}
