using System.Diagnostics.CodeAnalysis;
using Avalonia.Platform;
using LageBuch.AppLogic.Services;
using LageBuch.Speech;

namespace LageBuch.App.Services;

/// <summary>
/// Audio for the desktop deployment: synthesized speech where a voice is installed, and the bundled
/// clips where it is not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The clips stay.</b> They are what plays when speech is switched off, when the voice models
/// are missing, and when synthesis throws. Today's guarantee is that a cue is never silently lost,
/// and adding a voice must not weaken it.
/// </para>
/// <para>
/// <b>The voice loads lazily, on the audio worker.</b> A Piper model takes around two seconds to
/// load; doing that at startup would delay the window for a cue that may never fire. The first
/// <see cref="Play(AlarmSound, string?)"/> pays it instead, on <see cref="SerialAudioQueue"/>'s
/// thread, where blocking is already the norm.
/// </para>
/// </remarks>
internal sealed class SystemAlarmService : IAlarmService, IDisposable
{
    // One voice clip per AlarmSound. A missing entry or missing file just means that cue is silent.
    private static readonly IReadOnlyDictionary<AlarmSound, string> VoiceAssets =
        new Dictionary<AlarmSound, string>
        {
            [AlarmSound.IlsReminderDue] = "voice-rueckmeldung-ils.wav",

            // A plain tone, and the only cue whose clip is not speech. It is the fallback for when
            // there is no voice; with one, the cue says which task fell due, which is the point.
            [AlarmSound.TaskDue] = "alarm.wav",
            [AlarmSound.PressureCheckDue] = "voice-druckabfrage.wav",
            [AlarmSound.RetreatAlarm] = "voice-rueckzugsalarm.wav",
        };

    private readonly Dictionary<AlarmSound, byte[]> _voiceBytes = new();
    private readonly SerialAudioQueue _queue = new();
    private readonly WavePlayer _player = new();
    private readonly Lazy<ISpeechSynthesizer?> _speech;

    /// <param name="speechFactory">
    /// Builds the synthesizer, or returns null when no voice is installed. Called at most once, on
    /// the audio worker thread.
    /// </param>
    public SystemAlarmService(Func<ISpeechSynthesizer?>? speechFactory = null)
    {
        _speech = new Lazy<ISpeechSynthesizer?>(
            speechFactory ?? (static () => null),
            LazyThreadSafetyMode.ExecutionAndPublication);

        // Preload the voice clips (all platforms). Absent files are simply skipped.
        foreach (var (sound, file) in VoiceAssets)
        {
            if (TryLoad(new Uri($"avares://LageBuch.App/Assets/{file}")) is { } bytes)
            {
                _voiceBytes[sound] = bytes;
            }
        }
    }

    /// <remarks>
    /// Answers for the <em>configured</em> voice, not a loaded one: the model is loaded lazily, and
    /// forcing it here -- from the UI thread, to draw a button -- is exactly what the laziness
    /// exists to avoid.
    /// </remarks>
    public bool CanSpeak { get; init; }

    /// <summary>
    /// Why speech is off, when it is -- for the Stammdaten settings screen to show instead of
    /// leaving the switch quietly ineffective.
    /// </summary>
    public string? SpeechUnavailableReason { get; init; }

    public void Play(AlarmSound sound) => Play(sound, spokenDetail: null);

    public void Play(AlarmSound sound, string? spokenDetail)
    {
        var sentence = AlarmAnnouncements.Sentence(sound);
        var spoken = string.IsNullOrWhiteSpace(spokenDetail)
            ? sentence
            : $"{sentence}. {spokenDetail}";

        _voiceBytes.TryGetValue(sound, out var clip);
        _queue.Enqueue(() => SpeakOrPlay(sound.ToString(), spoken, clip));
    }

    public void Speak(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        _queue.Enqueue(() => SpeakOrPlay("vorlesen", text, fallbackClip: null));
    }

    public void StopSpeaking() => _queue.Drain();

    // Runs on the SerialAudioQueue's worker thread; blocks until the audio finishes playing.
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Any synthesis failure falls back to the bundled clip, or to silence; a failed cue never crashes the app.")]
    private void SpeakOrPlay(string name, string spoken, byte[]? fallbackClip)
    {
        try
        {
            if (_speech.Value is { } synthesizer && !string.IsNullOrWhiteSpace(spoken))
            {
                var audio = synthesizer.Synthesize(SpeechText.Spoken(spoken));
                _player.Play(name, audio.ToWav());
                return;
            }
        }
        catch
        {
            // A broken model, a missing file, a native fault: fall through to the bundled clip
            // rather than losing the cue.
        }

        if (fallbackClip is not null)
        {
            _player.Play(name, fallbackClip);
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
            return null; // asset not bundled -- that cue stays silent
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "Best-effort shutdown: a synthesizer that will not dispose must not stop the app from exiting.")]
    public void Dispose()
    {
        _player.Dispose();

        try
        {
            if (_speech.IsValueCreated)
            {
                _speech.Value?.Dispose();
            }
        }
        catch
        {
            // The process is going away regardless.
        }
    }
}
