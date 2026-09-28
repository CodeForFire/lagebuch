using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The join split in two (#459): <see cref="RemoteJoin.OpenAsync"/> reaches the host and reads its
/// Stammdaten and incident with nobody named yet, <see cref="RemoteJoin.ConnectAsync"/> then opens the
/// push channel for the Lagebuchführer chosen from them.
/// </summary>
public class RemoteJoinTests
{
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(10);

    private static LocalIncidentSession HostSession(FixedClock clock) =>
        TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

    [Fact]
    public async Task Opening_a_join_reads_the_hosts_stammdaten_and_incident_before_anyone_is_named()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        hostSession.SetKeyword("B3 Wohnung");
        var (host, port) = await TestHost.StartAsync(
            hostSession, clock, masterData: MasterDataSyncTests.SetWith("Host-Wache", 60));
        await using var _ = host;

        await using var join = await RemoteJoin.OpenAsync(
            "127.0.0.1", "1.0.0", new InMemoryTrustStore(), TestHost.DefaultPin, port);

        Assert.Equal(new[] { "Host-Wache" }, MasterDataJson.Parse(join.HostMasterDataJson).Brigades);
        Assert.Equal("B3 Wohnung", join.Incident.Keyword);
    }

    [Fact]
    public async Task What_the_host_changed_while_the_operator_was_chosen_is_there_after_connecting()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);

        await using var join = await RemoteJoin.OpenAsync(
            "127.0.0.1", "1.0.0", new InMemoryTrustStore(), port: host.Port);

        // Written on the host while this device's dialog still asked for a name: no hub was up to
        // carry a broadcast, and the poll is set past the end of the test, so only the catch-up
        // pass on connecting can bring it in.
        host.Current = SnapshotFixture.Revised(basis, 3, "Drei");

        await using var session = await join.ConnectAsync(
            new SessionOperator("Client", "RUF 1"), new ImmediateUiDispatcher(), reconcileInterval: Never);

        Assert.Equal(new[] { "Drei" }, SnapshotFixture.JournalOf(session));
    }

    [Fact]
    public async Task A_join_connects_only_once()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);

        await using var join = await RemoteJoin.OpenAsync(
            "127.0.0.1", "1.0.0", new InMemoryTrustStore(), port: host.Port);
        await using var session = await join.ConnectAsync(
            new SessionOperator("Client"), new ImmediateUiDispatcher(), reconcileInterval: Never);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            join.ConnectAsync(new SessionOperator("Client"), new ImmediateUiDispatcher(), reconcileInterval: Never));
    }

    [Fact]
    public async Task A_discarded_join_cannot_be_connected_afterwards()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);

        var join = await RemoteJoin.OpenAsync("127.0.0.1", "1.0.0", new InMemoryTrustStore(), port: host.Port);
        await join.DisposeAsync();
        await join.DisposeAsync(); // idempotent: the dialog's cancel and its teardown may both get here

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            join.ConnectAsync(new SessionOperator("Client"), new ImmediateUiDispatcher(), reconcileInterval: Never));
    }
}
