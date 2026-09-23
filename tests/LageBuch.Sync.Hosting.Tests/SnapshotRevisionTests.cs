using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The revision guard on the joined-client side (#295): a pushed snapshot the client has already
/// moved past is dropped instead of overwriting newer state. Driven through
/// <see cref="ScriptedSnapshotHost"/> because the real host cannot deliver one — see that type's
/// remarks for why, and for the alternatives that were rejected.
/// </summary>
public class SnapshotRevisionTests
{
    /// <summary>A snapshot of an incident with no journal, revision 0 — what a client joins onto.</summary>
    private static IncidentSnapshot BaseSnapshot()
    {
        var session = TestSession.StartNew(
            new InMemoryStore(),
            new FixedClock(),
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return SnapshotMapper.ToSnapshot(session.Incident);
    }

    // Journal entries are built directly rather than driven through a session, so every variant keeps
    // the same Incident.Id and the only thing that differs between them is what is being asserted.
    private static IncidentSnapshot Revised(IncidentSnapshot basis, long revision, params string[] journal) =>
        basis with
        {
            Revision = revision,
            Journal = journal
                .Select(text => new EtbEntryDto(
                    Guid.NewGuid(),
                    DateTimeOffset.UnixEpoch,
                    EtbDirection.Incoming,
                    text,
                    "Host (FFB 1)",
                    null,
                    null,
                    Array.Empty<EtbEntryEditDto>()))
                .ToArray(),
        };

    private static string[] JournalOf(RemoteIncidentSession client) =>
        client.Incident.Journal.Select(e => e.Text).ToArray();

    private static Task<RemoteIncidentSession> ConnectAsync(ScriptedSnapshotHost host) =>
        RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            port: host.Port);

    private static async Task WaitForClient(
        RemoteIncidentSession session,
        Func<bool> condition,
        string description,
        TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource();
        void Handler()
        {
            if (condition())
            {
                tcs.TrySetResult();
            }
        }

        // Subscribe before the first evaluation, so a broadcast can't slip through the gap between them.
        session.Changed += Handler;
        try
        {
            Handler();
            await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Timed out waiting for {description}.", ex);
        }
        finally
        {
            session.Changed -= Handler;
        }
    }

    [Fact]
    public async Task A_pushed_snapshot_below_the_applied_revision_is_discarded()
    {
        var basis = BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await ConnectAsync(host);

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        var five = Revised(basis, 5, "Fünf");
        host.Current = five;
        await host.PushAsync(five);
        await WaitForClient(client, () => JournalOf(client).Contains("Fünf"), "revision 5 to be applied");

        // A broadcast of revision 4 that was slow in flight while the host had already reached 5. The
        // negative is asserted by ordering an accepted revision 6 behind it rather than by sleeping:
        // SignalR delivers in order on one connection, so once 6 has landed, 4 has been and gone.
        await host.PushAsync(Revised(basis, 4, "Vier"));

        var six = Revised(basis, 6, "Sechs");
        host.Current = six;
        await host.PushAsync(six);
        await WaitForClient(client, () => JournalOf(client).Contains("Sechs"), "revision 6 to be applied");

        Assert.Equal(new[] { "Sechs" }, JournalOf(client));
        Assert.DoesNotContain("Vier", JournalOf(client));
        Assert.Equal(2, Volatile.Read(ref applied)); // 5 and 6 — the stale 4 raised no Changed at all
    }

    [Fact]
    public async Task A_pushed_snapshot_repeating_the_applied_revision_is_discarded()
    {
        var basis = BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await ConnectAsync(host);

        var seven = Revised(basis, 7, "Sieben");
        host.Current = seven;
        await host.PushAsync(seven);
        await WaitForClient(client, () => JournalOf(client).Contains("Sieben"), "revision 7 to be applied");

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        // Same revision, different content: the guard is <=, not <, so a duplicate delivery of the
        // revision already held cannot overwrite it.
        await host.PushAsync(Revised(basis, 7, "Sieben anders"));

        var eight = Revised(basis, 8, "Acht");
        host.Current = eight;
        await host.PushAsync(eight);
        await WaitForClient(client, () => JournalOf(client).Contains("Acht"), "revision 8 to be applied");

        Assert.Equal(new[] { "Acht" }, JournalOf(client));
        Assert.Equal(1, Volatile.Read(ref applied)); // 8 only
    }

    [Fact]
    public async Task The_initial_snapshots_revision_is_adopted_so_a_replay_of_it_is_discarded()
    {
        // Seeding _lastRevision from GET /snapshot is what stops the client treating everything up to
        // the joined-at revision as new; without it the first poll would also force a pointless resync.
        var basis = BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(Revised(basis, 12, "Zwölf"));
        await using var client = await ConnectAsync(host);

        Assert.Equal(new[] { "Zwölf" }, JournalOf(client));

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        await host.PushAsync(Revised(basis, 12, "Zwölf anders"));

        var thirteen = Revised(basis, 13, "Dreizehn");
        host.Current = thirteen;
        await host.PushAsync(thirteen);
        await WaitForClient(client, () => JournalOf(client).Contains("Dreizehn"), "revision 13 to be applied");

        Assert.Equal(1, Volatile.Read(ref applied));
    }
}
