using System.Buffers.Binary;

namespace LageBuch.Speech;

/// <summary>
/// One synthesized utterance: mono float samples in [-1, 1] plus the rate they were produced at.
/// </summary>
/// <remarks>
/// <para>
/// Kept engine-agnostic on purpose. Piper voices come back at 22.05 kHz and Supertonic at 44.1 kHz,
/// and the playback path must not care which: it writes a WAV header from <see cref="SampleRate"/>
/// and hands the bytes to the platform player.
/// </para>
/// <para>
/// <see cref="Samples"/> is a <see cref="ReadOnlyMemory{T}"/> rather than a <c>float[]</c> so the
/// synthesizer's buffer can be handed over without a defensive copy of a multi-second utterance,
/// while still not being writable through this record.
/// </para>
/// </remarks>
public sealed record SpeechAudio(ReadOnlyMemory<float> Samples, int SampleRate)
{
    public TimeSpan Duration =>
        SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);

    /// <summary>
    /// Renders a 16-bit PCM mono RIFF/WAVE file.
    /// </summary>
    /// <remarks>
    /// The desktop audio path already speaks WAV -- winmm plays it straight from memory, and
    /// <c>afplay</c>/<c>aplay</c> take it from a temp file -- so emitting WAV lets synthesized
    /// speech reuse the player the bundled cue clips already go through, rather than introducing a
    /// second audio backend.
    /// </remarks>
    public byte[] ToWav()
    {
        const int BitsPerSample = 16;
        const int Channels = 1;

        var samples = Samples.Span;
        var dataBytes = samples.Length * sizeof(short);
        var buffer = new byte[44 + dataBytes];
        var span = buffer.AsSpan();

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + dataBytes);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);          // PCM chunk size
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);           // PCM, uncompressed
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], SampleRate * Channels * BitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], Channels * BitsPerSample / 8);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], BitsPerSample);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], dataBytes);

        for (var i = 0; i < samples.Length; i++)
        {
            // Clamp before scaling: a vocoder can overshoot [-1, 1] slightly, and letting that wrap
            // turns a loud syllable into a burst of noise.
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(span[(44 + (i * 2))..], (short)(clamped * short.MaxValue));
        }

        return buffer;
    }
}
