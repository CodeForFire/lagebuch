namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The revision guard on the joined-client side (#295): a pushed snapshot the client has already
/// moved past is dropped instead of overwriting newer state. Driven through
/// <see cref="ScriptedSnapshotHost"/> because the real host cannot deliver one — see that type's
/// remarks for why, and for the alternatives that were rejected.
/// </summary>
public class SnapshotRevisionTests
{
    [Fact]
    public async Task A_pushed_snapshot_below_the_applied_revision_is_discarded()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host);

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        var five = SnapshotFixture.Revised(basis, 5, "Fünf");
        host.Current = five;
        await host.PushAsync(five);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Fünf"), "revision 5 to be applied");

        // A broadcast of revision 4 that was slow in flight while the host had already reached 5. The
        // negative is asserted by ordering an accepted revision 6 behind it rather than by sleeping:
        // SignalR delivers in order on one connection, so once 6 has landed, 4 has been and gone.
        await host.PushAsync(SnapshotFixture.Revised(basis, 4, "Vier"));

        var six = SnapshotFixture.Revised(basis, 6, "Sechs");
        host.Current = six;
        await host.PushAsync(six);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Sechs"), "revision 6 to be applied");

        Assert.Equal(new[] { "Sechs" }, SnapshotFixture.JournalOf(client));
        Assert.DoesNotContain("Vier", SnapshotFixture.JournalOf(client));
        Assert.Equal(2, Volatile.Read(ref applied)); // 5 and 6 — the stale 4 raised no Changed at all
    }

    [Fact]
    public async Task A_pushed_snapshot_repeating_the_applied_revision_is_discarded()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(host);

        var seven = SnapshotFixture.Revised(basis, 7, "Sieben");
        host.Current = seven;
        await host.PushAsync(seven);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Sieben"), "revision 7 to be applied");

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        // Same revision, different content: the guard is <=, not <, so a duplicate delivery of the
        // revision already held cannot overwrite it.
        await host.PushAsync(SnapshotFixture.Revised(basis, 7, "Sieben anders"));

        var eight = SnapshotFixture.Revised(basis, 8, "Acht");
        host.Current = eight;
        await host.PushAsync(eight);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Acht"), "revision 8 to be applied");

        Assert.Equal(new[] { "Acht" }, SnapshotFixture.JournalOf(client));
        Assert.Equal(1, Volatile.Read(ref applied)); // 8 only
    }

    [Fact]
    public async Task The_initial_snapshots_revision_is_adopted_so_a_replay_of_it_is_discarded()
    {
        // Seeding _lastRevision from GET /snapshot is what stops the client treating everything up to
        // the joined-at revision as new; without it the first poll would also force a pointless resync.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var host = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.Revised(basis, 12, "Zwölf"));
        await using var client = await SnapshotFixture.ConnectAsync(host);

        Assert.Equal(new[] { "Zwölf" }, SnapshotFixture.JournalOf(client));

        var applied = 0;
        client.Changed += () => Interlocked.Increment(ref applied);

        await host.PushAsync(SnapshotFixture.Revised(basis, 12, "Zwölf anders"));

        var thirteen = SnapshotFixture.Revised(basis, 13, "Dreizehn");
        host.Current = thirteen;
        await host.PushAsync(thirteen);
        await SnapshotFixture.WaitForClient(client, () => SnapshotFixture.JournalOf(client).Contains("Dreizehn"), "revision 13 to be applied");

        Assert.Equal(1, Volatile.Read(ref applied));
    }
}
