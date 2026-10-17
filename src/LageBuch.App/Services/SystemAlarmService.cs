using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Services;

/// <summary>
/// Audio for the desktop deployment. One-shot spoken cues (<see cref="Play"/>) work on Windows,
/// macOS and Linux: winmm in-memory on Windows, and <c>afplay</c>/<c>aplay</c> on macOS/Linux fed
/// a WAV extracted to a temp file. Every path degrades to a silent no-op when the asset or the
/// player is missing, so a build with no voice clip yet — or a host without the CLI player —
/// simply stays quiet rather than crashing.
/// </summary>
internal sealed class SystemAlarmService : IAlarmService, IDisposable
{
    private const uint SndNodefault = 0x0002; // no default beep if it fails
    private const uint SndMemory = 0x0004;  // pszSound points to in-memory WAV

    // No SND_ASYNC: playback must block the queue's worker thread until the clip finishes,
    // so cues play one after another instead of overlapping (see SerialAudioQueue). The price is
    // that a Windows cue cannot be stopped once it has started: PlaySound ignores the queue's
    // cancellation, so a wedged audio device there can only be abandoned, not ended.

    // One voice clip per AlarmSound. A missing entry or missing file just means that cue is silent,
    // which is why SystemAlarmServiceTests checks that every file named here is in Assets/.
    internal static readonly IReadOnlyDictionary<AlarmSound, string> VoiceAssets =
        new Dictionary<AlarmSound, string>
        {
            // Says „Rückmeldung an die Leitstelle", which fits whatever Leitstelle is configured
            // (#400). Generated once with ElevenLabs (voice "Chris Norddeutscher"), levelled to
            // the clip it replaced.
            [AlarmSound.IlsReminderDue] = "voice-rueckmeldung-leitstelle.wav",

            // Spoken since #460: a bare tone told an operator on another tab nothing about what
            // had come due. Generated once with ElevenLabs (converted to 16-bit mono 22050 Hz, peak
            // -3 dBFS like the other clips), not at run time.
            [AlarmSound.TaskDue] = "voice-aufgabe-faellig.wav",
            [AlarmSound.PressureCheckDue] = "voice-druckabfrage.wav",
            [AlarmSound.RetreatAlarm] = "voice-rueckzugsalarm.wav",
        };

    private readonly Dictionary<AlarmSound, byte[]> _voiceBytes = new();
    private readonly Dictionary<AlarmSound, string> _voiceTempFiles = new();
    private readonly SerialAudioQueue _queue = new();

    public SystemAlarmService()
    {
        // Preload the voice clips (all platforms). Absent files are simply skipped.
        foreach (var (sound, file) in VoiceAssets)
        {
            if (TryLoad(new Uri($"avares://LageBuch.App/Assets/{file}")) is { } bytes)
            {
                _voiceBytes[sound] = bytes;
            }
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A missing player binary must stay silent (see comment); a failed alarm never crashes the app.")]
    public void Play(AlarmSound sound)
    {
        if (!_voiceBytes.TryGetValue(sound, out var bytes))
        {
            return; // no clip bundled for this cue yet
        }

        _queue.Enqueue(cancellationToken => _ = PlayBlocking(sound, bytes, cancellationToken));
    }

    /// <summary>
    /// Long enough to cover the queue's per-cue limit plus its grace, with cues already queued
    /// ahead; past it the test reports a failure rather than waiting on a cue the queue abandoned
    /// (a wedged Windows device cannot be stopped, see SND_ASYNC above).
    /// </summary>
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    public async Task<bool> PlayTestAsync(CancellationToken cancellationToken = default)
    {
        if (!_voiceBytes.TryGetValue(AlarmSound.RetreatAlarm, out var bytes))
        {
            return false;
        }

        // Through the queue like any cue, so a test never talks over a real alarm.
        var played = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(token =>
        {
            try
            {
                played.TrySetResult(PlayBlocking(AlarmSound.RetreatAlarm, bytes, token));
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

    // Runs on the SerialAudioQueue's worker thread; blocks until the clip finishes playing, or
    // until the queue cancels a cue that overran its time. True when a player ran it to its end.
    private bool PlayBlocking(AlarmSound sound, byte[] bytes, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            // Not cancellable; see the note on SND_ASYNC above. False when winmm could not play it,
            // e.g. no output device.
            return PlaySound(bytes, IntPtr.Zero, SndMemory | SndNodefault);
        }

        var path = TempFileFor(sound, bytes);
        if (path is null)
        {
            return false;
        }

        var player = OperatingSystem.IsMacOS() ? "afplay" : "aplay";
        return RunPlayer(
            new ProcessStartInfo(player, $"\"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs a CLI audio player to its end, killing it as soon as
    /// <paramref name="cancellationToken"/> is cancelled — a hung <c>aplay</c> on a blocked audio
    /// device would otherwise hold its thread for good.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the player ran to its end and exited 0; <see langword="false"/>
    /// when it was killed, failed, or could not be started at all (e.g. the binary is missing),
    /// which stays silent.
    /// </returns>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Player binary missing (e.g. a headless Linux host without ALSA) — stay silent; the false return is the documented outcome.")]
    internal static bool RunPlayer(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            using (cancellationToken.Register(() => TryKill(process)))
            {
                process.WaitForExit();
            }

            // A player that exits non-zero, as aplay does without a usable sound device, played
            // nothing (#603).
            return !cancellationToken.IsCancellationRequested && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Runs as the cancellation callback, so it must never throw: an exception here would surface
    // from the queue's Cancel() call instead. The usual failure is a player that has just exited.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A player that already exited, or cannot be killed, is left alone; RunPlayer's WaitForExit is the outcome either way.")]
    private static void TryKill(Process process)
    {
        try
        {
            process.Kill();
        }
        catch
        {
            // Already exited, or no longer ours to kill.
        }
    }

    // afplay/aplay need a file path, so materialize the embedded WAV to a temp file once and cache it.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort temp cache: a failure falls back to silent alarm.")]
    internal string? TempFileFor(AlarmSound sound, byte[] bytes)
    {
        if (_voiceTempFiles.TryGetValue(sound, out var cached))
        {
            return cached;
        }

        try
        {
            var path = Path.Join(Path.GetTempPath(), $"lagebuch-{sound}.wav");
            File.WriteAllBytes(path, bytes);
            _voiceTempFiles[sound] = path;
            return path;
        }
        catch
        {
            return null;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A missing bundled asset must stay silent (see comment).")]
    private static byte[]? TryLoad(Uri asset)
    {
        try
        {
            using var stream = AssetLoader.Open(asset);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null; // asset not bundled — that cue stays silent
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort temp cleanup on shutdown: a failed delete must not stop the app from exiting.")]
    public void Dispose()
    {
        foreach (var path in _voiceTempFiles.Values)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort: the OS reclaims the temp directory eventually regardless.
            }
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[]? data, IntPtr hModule, uint flags);
}
