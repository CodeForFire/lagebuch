namespace LageBuch.Speech.Tests;

// The WAV header is what the desktop player consumes -- winmm reads it straight from memory, and
// afplay/aplay from a temp file -- so a wrong field here is silence or noise at an Einsatzstelle,
// not a compile error.
public class SpeechAudioTests
{
    [Fact]
    public void Duration_follows_the_sample_rate()
    {
        var audio = new SpeechAudio(new float[22050], 22050);
        Assert.Equal(1.0, audio.Duration.TotalSeconds, precision: 3);
    }

    [Fact]
    public void A_zero_sample_rate_does_not_divide_by_zero() =>
        Assert.Equal(TimeSpan.Zero, new SpeechAudio(new float[10], 0).Duration);

    [Fact]
    public void The_header_describes_16_bit_mono_pcm()
    {
        var wav = new SpeechAudio(new float[8], 44100).ToWav();

        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
        Assert.Equal("fmt "u8.ToArray(), wav[12..16]);
        Assert.Equal("data"u8.ToArray(), wav[36..40]);

        Assert.Equal(16, BitConverter.ToInt32(wav, 16));            // PCM chunk size
        Assert.Equal(1, BitConverter.ToInt16(wav, 20));             // uncompressed
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));             // mono
        Assert.Equal(44100, BitConverter.ToInt32(wav, 24));         // sample rate
        Assert.Equal(44100 * 2, BitConverter.ToInt32(wav, 28));     // byte rate
        Assert.Equal(2, BitConverter.ToInt16(wav, 32));             // block align
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));            // bits per sample
    }

    [Fact]
    public void The_declared_sizes_match_the_actual_bytes()
    {
        var wav = new SpeechAudio(new float[100], 22050).ToWav();

        Assert.Equal(44 + 200, wav.Length);
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));  // RIFF size excludes its own 8
        Assert.Equal(200, BitConverter.ToInt32(wav, 40));            // data size
    }

    [Fact]
    public void Full_scale_samples_reach_the_ends_of_the_range()
    {
        var wav = new SpeechAudio(new[] { 1f, -1f, 0f }, 22050).ToWav();

        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 44));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(wav, 46));
        Assert.Equal(0, BitConverter.ToInt16(wav, 48));
    }

    // A vocoder can overshoot [-1, 1]. Wrapping instead of clamping turns a loud syllable into a
    // burst of noise, which on an alarm cue is worse than the overshoot.
    [Fact]
    public void An_overshooting_sample_clamps_rather_than_wrapping()
    {
        var wav = new SpeechAudio(new[] { 1.8f, -2.5f }, 22050).ToWav();

        Assert.Equal(short.MaxValue, BitConverter.ToInt16(wav, 44));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(wav, 46));
    }

    [Fact]
    public void An_empty_utterance_still_yields_a_valid_header()
    {
        var wav = new SpeechAudio(Array.Empty<float>(), 22050).ToWav();

        Assert.Equal(44, wav.Length);
        Assert.Equal(0, BitConverter.ToInt32(wav, 40));
    }
}
