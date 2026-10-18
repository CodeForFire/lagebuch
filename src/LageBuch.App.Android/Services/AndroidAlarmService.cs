using System.Diagnostics.CodeAnalysis;
using Android.Content;
using Android.Media;
using LageBuch.App.Shared.Services;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Android.Services;

/// <summary>
/// Audio for the Android deployment: the same spoken clips as the desktop (<see cref="VoiceClips"/>),
/// played one after another through a <see cref="SerialAudioQueue"/> with Android's own
/// <see cref="MediaPlayer"/> (#637). Playback goes out on the alarm stream, so a crew that turned the
/// media volume down for a video still hears the Rückzugsalarm; it follows the alarm volume instead.
/// Do Not Disturb is not overridden: Android lets alarms through it by default, and forcing it would
/// need the notification-policy permission for a setting the device owner chose.
/// </summary>
/// <remarks>
/// One instance per process (see <c>MainActivity</c>): the queue's worker thread lives as long as the
/// process, and the Activity is recreated on every configuration change it does not handle.
/// </remarks>
[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "Process-lifetime singleton, like SerialAudioQueue itself: the queue's worker drains until the process dies.")]
public sealed class AndroidAlarmService : IAlarmService
{
    /// <summary>Same cap as the desktop's "Ton testen": the queue's per-cue limit and grace, with cues ahead.</summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private readonly Dictionary<AlarmSound, byte[]> _voiceBytes = new();
    private readonly string _clipDir;
    private readonly SerialAudioQueue _queue = new();

    public AndroidAlarmService(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // MediaPlayer plays from a path, so each clip is written to the app's cache before its first
        // use (on the queue's thread, see ClipFile). Absent clips are skipped and stay silent.
        _clipDir = System.IO.Path.Join(AndroidAppPaths.CacheDir(context), "voice");
        foreach (var sound in VoiceClips.Files.Keys)
        {
            if (VoiceClips.TryLoad(sound) is { } bytes)
            {
                _voiceBytes[sound] = bytes;
            }
        }
    }

    public void Play(AlarmSound sound)
    {
        if (!_voiceBytes.ContainsKey(sound))
        {
            return; // no clip bundled for this cue
        }

        _queue.Enqueue(cancellationToken => _ = PlayBlocking(sound, cancellationToken));
    }

    public async Task<bool> PlayTestAsync(CancellationToken cancellationToken = default)
    {
        if (!_voiceBytes.ContainsKey(AlarmSound.RetreatAlarm))
        {
            return false;
        }

        // Through the queue like any cue, so a test never talks over a real alarm.
        var played = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(token =>
        {
            try
            {
                played.TrySetResult(PlayBlocking(AlarmSound.RetreatAlarm, token));
            }
            finally
            {
                // A cue that threw reports a failure instead of leaving the test waiting.
                played.TrySetResult(false);
            }
        });

        try
        {
            return await played.Task.WaitAsync(TestTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // Runs on the SerialAudioQueue's worker thread and blocks until the clip has played, failed, or
    // been cancelled for overrunning. True only when the player reached the end of the clip.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A device that cannot play (no output, a codec failure) stays silent; the false return reaches \"Ton testen\", and an alarm never crashes the app.")]
    private bool PlayBlocking(AlarmSound sound, CancellationToken cancellationToken)
    {
        var path = ClipFile(sound);
        if (path is null)
        {
            return false;
        }

        try
        {
            var builder = new AudioAttributes.Builder();
            builder.SetUsage(AudioUsageKind.Alarm);
            builder.SetContentType(AudioContentType.Speech);
            using var attributes = builder.Build();
            if (attributes is null)
            {
                return false;
            }

            // The worker thread has no Looper, so MediaPlayer raises Completion and Error on the main
            // thread, which this thread does not block.
            using var ended = new ManualResetEventSlim();
            var completed = false;
            using var player = new MediaPlayer();
            player.Completion += (_, _) =>
            {
                completed = true;
                ended.Set();
            };
            player.Error += (_, e) =>
            {
                e.Handled = true; // handled here, so the player does not also report Completion
                ended.Set();
            };

            try
            {
                player.SetAudioAttributes(attributes);
                player.SetDataSource(path);
                player.Prepare();
                player.Start();
                try
                {
                    ended.Wait(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // The queue gave up on an overrunning cue: end it before the next one starts.
                    player.Stop();
                }

                return completed;
            }
            finally
            {
                player.Release();
            }
        }
        catch
        {
            return false;
        }
    }

    // Written again whenever it is missing: Android may clear the cache while the app runs.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort cache file: a failed write leaves that cue silent, as a missing clip does.")]
    private string? ClipFile(AlarmSound sound)
    {
        if (!_voiceBytes.TryGetValue(sound, out var bytes) || !VoiceClips.Files.TryGetValue(sound, out var file))
        {
            return null;
        }

        try
        {
            var path = System.IO.Path.Join(_clipDir, file);
            if (!File.Exists(path))
            {
                // Through a temp name, so a write cut short never leaves a truncated clip behind.
                System.IO.Directory.CreateDirectory(_clipDir);
                File.WriteAllBytes(path + ".tmp", bytes);
                File.Move(path + ".tmp", path, overwrite: true);
            }

            return path;
        }
        catch
        {
            return null;
        }
    }
}
