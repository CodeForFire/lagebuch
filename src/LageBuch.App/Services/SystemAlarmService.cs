using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Services;

/// <summary>
/// Audio for the desktop deployment. The looping life-safety alarm (<see cref="Start"/>/
/// <see cref="Stop"/>) uses winmm's PlaySound on Windows and is a no-op elsewhere, unchanged.
/// The one-shot spoken cues (<see cref="Play"/>) work on Windows, macOS and Linux: winmm in-memory
/// on Windows, and <c>afplay</c>/<c>aplay</c> on macOS/Linux fed a WAV extracted to a temp file.
/// Every path degrades to a silent no-op when the asset or the player is missing, so a build with
/// no voice clip yet — or a host without the CLI player — simply stays quiet rather than crashing.
/// </summary>
internal sealed class SystemAlarmService : IAlarmService
{
    private const uint SndAsync = 0x0001;   // play asynchronously
    private const uint SndNodefault = 0x0002; // no default beep if it fails
    private const uint SndMemory = 0x0004;  // pszSound points to in-memory WAV
    private const uint SndLoop = 0x0008;    // loop until the next PlaySound call

    private static readonly Uri AlarmAsset = new("avares://LageBuch.App/Assets/alarm.wav");

    // One voice clip per AlarmSound. A missing entry or missing file just means that cue is silent.
    private static readonly IReadOnlyDictionary<AlarmSound, string> VoiceAssets =
        new Dictionary<AlarmSound, string>
        {
            [AlarmSound.IlsReminderDue] = "voice-rueckmeldung-ils.wav",
            // Generic tone (already bundled) — a task falling due is frequent enough that a spoken
            // sentence would be more noise than signal.
            [AlarmSound.TaskDue] = "alarm.wav",
        };

    private readonly byte[]? _wav;
    private readonly Dictionary<AlarmSound, byte[]> _voiceBytes = new();
    private readonly Dictionary<AlarmSound, string> _voiceTempFiles = new();
    private bool _sounding;

    public SystemAlarmService()
    {
        // Load the looping alarm WAV once. Only needed on Windows; skip the work elsewhere.
        if (OperatingSystem.IsWindows())
            _wav = TryLoad(AlarmAsset);

        // Preload the voice clips (all platforms). Absent files are simply skipped.
        foreach (var (sound, file) in VoiceAssets)
            if (TryLoad(new Uri($"avares://LageBuch.App/Assets/{file}")) is { } bytes)
                _voiceBytes[sound] = bytes;
    }

    public void Start()
    {
        if (_sounding || _wav is null || !OperatingSystem.IsWindows())
            return;
        PlaySound(_wav, IntPtr.Zero, SndAsync | SndMemory | SndLoop | SndNodefault);
        _sounding = true;
    }

    public void Stop()
    {
        if (!_sounding || !OperatingSystem.IsWindows())
            return;
        PlaySound(null, IntPtr.Zero, 0); // null sound stops any current playback
        _sounding = false;
    }

    [SuppressMessage("Design", "CA1031",
        Justification = "A missing player binary must stay silent (see comment); a failed alarm never crashes the app.")]
    public void Play(AlarmSound sound)
    {
        if (!_voiceBytes.TryGetValue(sound, out var bytes))
            return; // no clip bundled for this cue yet

        if (OperatingSystem.IsWindows())
        {
            // One-shot (no SndLoop). Deliberately distinct from Start's looping playback.
            PlaySound(bytes, IntPtr.Zero, SndAsync | SndMemory | SndNodefault);
            return;
        }

        var path = TempFileFor(sound, bytes);
        if (path is null)
            return;

        var player = OperatingSystem.IsMacOS() ? "afplay" : "aplay";
        try
        {
            Process.Start(new ProcessStartInfo(player, $"\"{path}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch
        {
            // Player binary missing (e.g. a headless Linux host without ALSA) — stay silent.
        }
    }

    // afplay/aplay need a file path, so materialize the embedded WAV to a temp file once and cache it.
    [SuppressMessage("Design", "CA1031",
        Justification = "Best-effort temp cache: a failure falls back to silent alarm.")]
    private string? TempFileFor(AlarmSound sound, byte[] bytes)
    {
        if (_voiceTempFiles.TryGetValue(sound, out var cached))
            return cached;
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"lagebuch-{sound}.wav");
            File.WriteAllBytes(path, bytes);
            _voiceTempFiles[sound] = path;
            return path;
        }
        catch
        {
            return null;
        }
    }

    [SuppressMessage("Design", "CA1031",
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

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool PlaySound(byte[]? data, IntPtr hModule, uint flags);
}
