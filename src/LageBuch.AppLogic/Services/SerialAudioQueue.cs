using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Runs enqueued playback actions one at a time, in FIFO order, on a single dedicated
/// background thread — so cues that become due close together play sequentially instead of
/// overlapping. <see cref="Enqueue"/> itself never blocks the caller.
///
/// Each action gets a bounded time to finish (see constructor). If it overruns (e.g. a stuck OS
/// audio player process that never exits), its <see cref="CancellationToken"/> is cancelled so
/// the player can be stopped, and the queue waits a short grace for that before moving on — so
/// one stuck cue can neither permanently silence later ones nor keep its thread for good.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "App-lifetime singleton: its worker thread drains until process shutdown, so the owning BlockingCollection is intentionally never disposed.")]
[SuppressMessage(
    "Naming",
    "CA1711",
    Justification = "It is a FIFO queue of playback actions, not a type posing as a collection; public only so both heads share it (#637).")]
public sealed class SerialAudioQueue
{
    private readonly BlockingCollection<Action<CancellationToken>> _queue = new();
    private readonly TimeSpan _perItemTimeout;
    private readonly TimeSpan _cancelGrace;

    /// <param name="perItemTimeout">How long a cue may play before it is told to stop.</param>
    /// <param name="cancelGrace">
    /// How long a cue that was told to stop may take to end before the queue serves the next one
    /// anyway. A cue that ignores its token (Windows' <c>PlaySound</c> cannot be interrupted) is
    /// then abandoned on its background thread.
    /// </param>
    public SerialAudioQueue(TimeSpan? perItemTimeout = null, TimeSpan? cancelGrace = null)
    {
        _perItemTimeout = perItemTimeout ?? TimeSpan.FromSeconds(10);
        _cancelGrace = cancelGrace ?? TimeSpan.FromSeconds(2);
        var worker = new Thread(Run) { IsBackground = true, Name = nameof(SerialAudioQueue) };
        worker.Start();
    }

    /// <summary>
    /// Queues <paramref name="play"/> to run after everything already queued. The token it is
    /// handed is cancelled once the cue overruns its time; <paramref name="play"/> should then stop
    /// playback and return.
    /// </summary>
    public void Enqueue(Action<CancellationToken> play) => _queue.Add(play);

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A misbehaving or hanging cue must not stop the worker from serving the next one (the per-item timeout has already elapsed).")]
    private void Run()
    {
        foreach (var play in _queue.GetConsumingEnumerable())
        {
            using var cts = new CancellationTokenSource();
            var token = cts.Token;

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
                    play(token);
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

            if (!worker.Join(_perItemTimeout))
            {
                // The cue overran (a stuck OS player that never exits): tell it to stop, and give
                // it a moment to do so, so the next cue does not talk over it. One that still does
                // not end is abandoned -- background, so it cannot hold up shutdown -- and the next
                // cue is served anyway.
                cts.Cancel();
                worker.Join(_cancelGrace);
            }
        }
    }
}
