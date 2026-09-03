using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.AppLogic.Tests;

// IncidentStore.Save must never block its caller on the underlying (potentially slow) write — see
// issue #167 P0 #1. These use the internal writer-injecting constructor to control timing/failure
// deterministically, the same way SerialAudioQueueTests controls SerialAudioQueue.
public class IncidentStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Save_returns_before_the_write_is_durable()
    {
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var written = new List<string>();
        var store = new IncidentStore((path, incident) =>
        {
            started.Set();
            release.Wait();
            written.Add(path);
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        store.Save("/x.fwincident", Incident.Start(new FixedClock(T0), new SessionOperator("Müller")));
        sw.Stop();

        Assert.True(started.Wait(TimeSpan.FromSeconds(1)), "write never started");
        Assert.True(sw.Elapsed < TimeSpan.FromMilliseconds(500), "Save blocked the caller");
        Assert.Empty(written); // still stuck behind `release` — not durable yet

        release.Set();
    }

    [Fact]
    public async Task After_FlushAsync_the_write_is_durable()
    {
        var written = new List<string>();
        var store = new IncidentStore((path, incident) => written.Add(path));

        store.Save("/x.fwincident", Incident.Start(new FixedClock(T0), new SessionOperator("Müller")));
        await store.FlushAsync();

        Assert.Equal(new[] { "/x.fwincident" }, written);
    }

    [Fact]
    public async Task Two_rapid_saves_land_in_fifo_order()
    {
        var written = new List<string>();
        var store = new IncidentStore((path, incident) => written.Add(path));

        for (var i = 0; i < 5; i++)
        {
            store.Save($"/{i}.fwincident", Incident.Start(new FixedClock(T0), new SessionOperator("Müller")));
        }

        await store.FlushAsync();

        Assert.Equal(new[] { "/0.fwincident", "/1.fwincident", "/2.fwincident", "/3.fwincident", "/4.fwincident" }, written);
    }

    [Fact]
    public async Task A_failing_write_raises_SaveFailed_and_does_not_wedge_the_queue()
    {
        var goodWrites = new List<string>();
        var store = new IncidentStore((path, incident) =>
        {
            if (path == "/bad.fwincident")
            {
                throw new InvalidOperationException("disk full");
            }

            goodWrites.Add(path);
        });
        Exception? failure = null;
        store.SaveFailed += ex => failure = ex;

        store.Save("/bad.fwincident", Incident.Start(new FixedClock(T0), new SessionOperator("Müller")));
        store.Save("/good.fwincident", Incident.Start(new FixedClock(T0), new SessionOperator("Müller")));
        await store.FlushAsync();

        Assert.NotNull(failure);
        Assert.Equal("disk full", failure!.Message);
        Assert.Equal(new[] { "/good.fwincident" }, goodWrites);
    }

    [Fact]
    public async Task The_real_writer_round_trips_through_IncidentRepository()
    {
        var path = Path.Combine(Path.GetTempPath(), $"store-{Guid.NewGuid():N}.fwincident");
        try
        {
            var store = new IncidentStore();
            var incident = Incident.Start(new FixedClock(T0), new SessionOperator("Müller"));
            incident.SetIncidentNumber(new IncidentNumber("B 1.2 260715 4242"));

            store.Save(path, incident);
            await store.FlushAsync();

            Assert.Equal("B 1.2 260715 4242", store.Load(path).IncidentNumber!.Value);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FixedClock : IClock
    {
        public FixedClock(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }
    }
}
