using System.Diagnostics.CodeAnalysis;

namespace LageBuch.Sync.Tests;

/// <summary>
/// The joined client's one ordering rule (#295). Kept pure so that the full space of reorderings can be
/// explored here; the Kestrel tests then only have to prove the wiring.
/// </summary>
public class SyncPositionTests
{
    private static readonly Guid EpochA = new("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid EpochB = new("bbbbbbbb-0000-0000-0000-000000000000");

    [Theory]
    [InlineData(5, 6, true)]
    [InlineData(5, 5, false)]
    [InlineData(5, 4, false)]
    public void Within_one_epoch_only_a_newer_revision_supersedes(long held, long incoming, bool supersedes) =>
        Assert.Equal(supersedes, new SyncPosition(EpochA, incoming).Supersedes(new SyncPosition(EpochA, held)));

    [Theory]
    [InlineData(5, 6)]
    [InlineData(5, 5)]
    [InlineData(5, 1)]
    public void A_different_epoch_supersedes_whatever_its_revision(long held, long incoming) =>
        Assert.True(new SyncPosition(EpochB, incoming).Supersedes(new SyncPosition(EpochA, held)));

    /// <summary>
    /// A seeded simulation of everything the network may do to snapshots on their way to a client:
    /// each one the host produces may be delivered on any of three channels (hub push, command
    /// response, resync), any number of times — including never — in any order, and the host may
    /// restart sharing in between. Two properties must hold for every run:
    /// within one epoch the client never goes backwards, and one reconcile pass at the end lands it
    /// exactly on the host's current position.
    /// </summary>
    [Fact]
    [SuppressMessage("Security", "CA5394", Justification = "A seeded simulation, not a secret: a fixed seed is what lets a failing run replay exactly.")]
    public void Any_reordering_duplication_or_loss_never_regresses_and_heals_in_one_reconcile()
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var random = new Random(seed);
            var produced = new List<SyncPosition>();
            var epoch = NextGuid(random);
            var revision = 0L;
            var current = new SyncPosition(epoch, revision);
            var applied = current; // the client joined at the host's position

            var steps = random.Next(1, 40);
            for (var i = 0; i < steps; i++)
            {
                if (random.Next(10) == 0)
                {
                    // Restart sharing: a new epoch, whose counter carries on from wherever the new
                    // instance starts — possibly below, at, or above the client's.
                    epoch = NextGuid(random);
                    revision = random.Next(0, 10);
                }
                else
                {
                    revision++;
                }

                current = new SyncPosition(epoch, revision);
                produced.Add(current);
            }

            // Every copy the network delivers: 0-3 per produced snapshot, shuffled across channels.
            var deliveries = produced
                .SelectMany(p => Enumerable.Repeat(p, random.Next(0, 4)))
                .OrderBy(_ => random.Next())
                .ToList();

            foreach (var incoming in deliveries)
            {
                var before = applied;
                if (incoming.Supersedes(applied))
                {
                    applied = incoming;
                }

                Assert.False(
                    applied.Epoch == before.Epoch && applied.Revision < before.Revision,
                    $"seed {seed}: went back from {before} to {applied}");
            }

            // The reconcile pass: the host says where it is; any difference fetches its current
            // snapshot, which is then offered to the same rule as everything else.
            if (current != applied && current.Supersedes(applied))
            {
                applied = current;
            }

            Assert.True(current == applied, $"seed {seed}: host at {current}, client left at {applied}");
        }
    }

    // Drawn from the seeded generator rather than Guid.NewGuid, so a failing seed replays exactly.
    [SuppressMessage("Security", "CA5394", Justification = "A seeded simulation, not a secret: a fixed seed is what lets a failing run replay exactly.")]
    private static Guid NextGuid(Random random)
    {
        var bytes = new byte[16];
        random.NextBytes(bytes);
        return new Guid(bytes);
    }
}
