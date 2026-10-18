using System.Diagnostics.CodeAnalysis;
using Avalonia.Platform;
using LageBuch.AppLogic.Services;

namespace LageBuch.App.Shared.Services;

/// <summary>
/// The bundled spoken cue for each <see cref="AlarmSound"/>. Shared by the desktop and Android
/// heads, which differ only in how they play a clip, so both say the same thing (#637).
/// </summary>
public static class VoiceClips
{
    // One voice clip per AlarmSound. A missing entry or missing file just means that cue is silent,
    // which is why SystemAlarmServiceTests checks that every file named here is in Assets/.
    public static readonly IReadOnlyDictionary<AlarmSound, string> Files =
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

    /// <summary>
    /// The WAV bytes of the clip for <paramref name="sound"/>, or <see langword="null"/> when none
    /// is mapped or bundled — that cue then stays silent.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A missing bundled asset must stay silent; the null return is the documented outcome.")]
    public static byte[]? TryLoad(AlarmSound sound)
    {
        if (!Files.TryGetValue(sound, out var file))
        {
            return null;
        }

        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://LageBuch.App.Shared/Assets/{file}"));
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null; // asset not bundled — that cue stays silent
        }
    }
}
