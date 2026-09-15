using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace LageBuch.App.Services;

/// <summary>
/// Runs enqueued playback actions one at a time, in FIFO order, on a single dedicated
/// background thread — so cues that become due close together play sequentially instead of
/// overlapping. <see cref="Enqueue"/> itself never blocks the caller.
///
/// Each action gets a bounded time to finish (see constructor); if it hangs (e.g. a stuck OS
/// audio player process that never exits), the queue moves on to the next item anyway so one
/// stuck cue can't permanently silence later ones.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "App-lifetime singleton: its worker thread drains until process shutdown, so the owning BlockingCollection is intentionally never disposed.")]
internal sealed class SerialAudioQueue
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly TimeSpan _perItemTimeout;

    public SerialAudioQueue(TimeSpan? perItemTimeout = null)
    {
        _perItemTimeout = perItemTimeout ?? TimeSpan.FromSeconds(10);
        var worker = new Thread(Run) { IsBackground = true, Name = nameof(SerialAudioQueue) };
        worker.Start();
    }

    /// <summary>Queues <paramref name="play"/> to run after everything already queued.</summary>
    public void Enqueue(Action play) => _queue.Add(play);

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A misbehaving or hanging cue must not stop the worker from serving the next one (the per-item timeout has already elapsed).")]
    private void Run()
    {
        foreach (var play in _queue.GetConsumingEnumerable())
        {
            // A dedicated thread per cue rather than Task.Run: the cue has to run somewhere the
            // watchdog below can abandon, and the thread pool is the wrong somewhere. Cues are
            // driven by alarms, which fire exactly when the app is busiest -- and a pool saturated
            // by other work hands out threads only as fast as it grows them (a thread or two per
            // second), so a cue could sit unplayed for seconds, or outlast its own watchdog without
            // ever having started. An alarm that does not sound is the one failure this class
            // exists to prevent. Cues are rare and short, so a thread each is well affordable.
            var worker = new Thread(() =>
            {
                try
                {
                    play();
                }
                catch
                {
                    // A misbehaving cue must not stop the queue from serving the next one.
                }
            })
            {
                IsBackground = true,
                Name = $"{nameof(SerialAudioQueue)} item",
            };

            worker.Start();

            // Bounded wait, not a join: if the cue hangs (a stuck OS player that never exits) the
            // thread is abandoned -- background, so it cannot hold up shutdown -- and the next cue
            // is served anyway.
            worker.Join(_perItemTimeout);
        }
    }
}
