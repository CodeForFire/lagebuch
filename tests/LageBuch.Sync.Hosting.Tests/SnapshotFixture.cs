using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// Shared scaffolding for the tests that drive a <see cref="ScriptedSnapshotHost"/>: building
/// snapshots at chosen revisions, connecting a real client to one, and waiting on the client's state
/// rather than on a broadcast count.
/// </summary>
internal static class SnapshotFixture
{
    /// <summary>A snapshot of an incident with no journal, revision 0 — what a client joins onto.</summary>
    public static IncidentSnapshot BaseSnapshot()
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

    /// <summary>
    /// The same incident at a chosen revision, carrying exactly the journal entries named. Built
    /// directly rather than driven through a session so every variant keeps one Incident.Id and the
    /// only thing differing between them is what the test asserts on.
    /// </summary>
    public static IncidentSnapshot Revised(IncidentSnapshot basis, long revision, params string[] journal) =>
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

    public static string[] JournalOf(RemoteIncidentSession client) =>
        client.Incident.Journal.Select(e => e.Text).ToArray();

    public static Task<RemoteIncidentSession> ConnectAsync(
        ScriptedSnapshotHost host, TimeSpan? reconcileInterval = null, TimeProvider? timeProvider = null) =>
        RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            port: host.Port,
            reconcileInterval: reconcileInterval,
            timeProvider: timeProvider);

    /// <summary>
    /// Waits until <paramref name="condition"/> holds on the client. Polls the predicate on every
    /// applied snapshot rather than counting broadcasts, because commands are fire-and-forget and the
    /// counts are not deterministic (#326).
    /// </summary>
    public static async Task WaitForClient(
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
}
