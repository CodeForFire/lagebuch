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
[SuppressMessage("Design", "CA1001",
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

    [SuppressMessage("Design", "CA1031",
        Justification = "A misbehaving or hanging cue must not stop the worker from serving the next one (the per-item timeout has already elapsed).")]
    private void Run()
    {
        foreach (var play in _queue.GetConsumingEnumerable())
        {
            try
            {
                Task.Run(play).Wait(_perItemTimeout);
            }
            catch
            {
                // A misbehaving cue must not stop the queue from serving the next one.
            }
        }
    }
}
