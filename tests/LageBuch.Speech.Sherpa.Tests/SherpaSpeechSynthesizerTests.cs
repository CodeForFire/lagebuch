namespace LageBuch.Speech.Sherpa.Tests;

// These need speech-models/ on disk, which is fetched rather than committed, so each one returns
// early when it is absent -- the same shape as the audition. Run:
//     packaging/speech/fetch-voices.sh speech-models thorsten-medium
public class SherpaSpeechSynthesizerTests
{
    // Regression guard for a bug the unit tests could never have caught. OfflineTtsConfig is a
    // struct, and the loader used to fill it in via a method taking it *by value*: the model paths
    // landed in a copy, sherpa-onnx got an empty config, and instead of complaining it returned no
    // audio at all. Asserting on real samples is the only thing that distinguishes "loaded and
    // spoke" from "silently configured nothing".
    [Fact]
    public void A_loaded_voice_actually_produces_audio()
    {
        if (TryLoad("thorsten-medium") is not { } synth)
        {
            return;
        }

        using (synth)
        {
            var audio = synth.Synthesize(SpeechText.Normalize("Rückmeldung an ILS fällig"));

            Assert.Equal(22050, audio.SampleRate);
            Assert.True(
                audio.Duration > TimeSpan.FromSeconds(0.5),
                $"Expected real speech, got {audio.Duration.TotalSeconds:F2}s.");
            Assert.Contains(audio.Samples.ToArray(), sample => Math.Abs(sample) > 0.01f);
        }
    }

    [Fact]
    public void The_wav_it_renders_carries_a_matching_header()
    {
        if (TryLoad("thorsten-medium") is not { } synth)
        {
            return;
        }

        using (synth)
        {
            var audio = synth.Synthesize("Rückzug");
            var wav = audio.ToWav();

            Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
            Assert.Equal("WAVE"u8.ToArray(), wav[8..12]);
            Assert.Equal(audio.SampleRate, BitConverter.ToInt32(wav, 24));
            Assert.Equal(16, BitConverter.ToInt16(wav, 34));                  // bits per sample
            Assert.Equal(audio.Samples.Length * 2, BitConverter.ToInt32(wav, 40));
            Assert.Equal(44 + (audio.Samples.Length * 2), wav.Length);
        }
    }

    [Fact]
    public void A_missing_voice_names_the_file_it_wanted()
    {
        if (ModelsRoot() is not { } root)
        {
            return;
        }

        var absent = new SpeechVoice(
            "gibt-es-nicht", "Nicht vorhanden", SpeechGender.Male, SpeechEngine.Vits, "CC0-1.0", "-");

        var ex = Assert.Throws<FileNotFoundException>(() => SherpaSpeechSynthesizer.Load(root, absent));
        Assert.Contains("gibt-es-nicht", ex.Message, StringComparison.Ordinal);
    }

    private static SherpaSpeechSynthesizer? TryLoad(string voiceId)
    {
        if (ModelsRoot() is not { } root)
        {
            return null;
        }

        var voice = VoiceCatalog.Candidates.Single(v => v.Id == voiceId);
        return Directory.Exists(SpeechModelLocator.VoiceDirectory(root, voice))
            ? SherpaSpeechSynthesizer.Load(root, voice)
            : null;
    }

    private static string? ModelsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Join(dir.FullName, SpeechModelLocator.ModelsDirectoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
