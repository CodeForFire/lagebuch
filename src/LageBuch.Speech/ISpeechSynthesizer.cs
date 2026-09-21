namespace LageBuch.Speech;

/// <summary>
/// Turns German text into audio. One instance holds one loaded voice.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately synchronous. Every caller already runs on <c>SerialAudioQueue</c>'s worker thread,
/// which exists so cues play one after another rather than overlapping; handing back a Task would
/// invite callers to fire several at once, which is the failure that queue prevents.
/// </para>
/// <para>
/// Implementations must be safe to call from that single worker thread only -- they are not
/// required to be thread-safe.
/// </para>
/// </remarks>
public interface ISpeechSynthesizer : IDisposable
{
    /// <summary>The loaded voice, for the settings UI and for licence attribution.</summary>
    SpeechVoice Voice { get; }

    /// <summary>
    /// Speaks <paramref name="text"/>, which the caller has already put through
    /// <see cref="SpeechText"/>. Passing raw UI text here is a bug, not a fallback.
    /// </summary>
    /// <param name="text">German text, already normalized.</param>
    /// <param name="speed">1.0 is the voice's own default pace; larger is faster.</param>
    SpeechAudio Synthesize(string text, float speed = 1.0f);
}
