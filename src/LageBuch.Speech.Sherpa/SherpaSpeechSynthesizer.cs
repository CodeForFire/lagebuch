using SherpaOnnx;

namespace LageBuch.Speech.Sherpa;

/// <summary>
/// An <see cref="ISpeechSynthesizer"/> backed by sherpa-onnx.
/// </summary>
/// <remarks>
/// Both model families run through one <c>OfflineTts</c>; only the config differs. A Piper/VITS
/// voice needs a model, a tokens.txt and the espeak-ng data directory. Supertonic needs its four
/// ONNX stages plus its own tokenizer, takes no espeak data at all, and carries the language in
/// the per-utterance generation config rather than the model config.
/// </remarks>
public sealed class SherpaSpeechSynthesizer : ISpeechSynthesizer
{
    private readonly OfflineTts _tts;

    private SherpaSpeechSynthesizer(OfflineTts tts, SpeechVoice voice)
    {
        _tts = tts;
        Voice = voice;
    }

    public SpeechVoice Voice { get; }

    /// <summary>The rate the loaded voice produces, for callers that need it before synthesizing.</summary>
    public int SampleRate => _tts.SampleRate;

    /// <summary>Speaker styles the loaded model offers. 1 for a single-speaker voice.</summary>
    public int SpeakerCount => _tts.NumSpeakers;

    /// <summary>
    /// Loads <paramref name="voice"/> from a models root resolved by <see cref="SpeechModelLocator"/>.
    /// </summary>
    /// <exception cref="FileNotFoundException">A file the voice needs is missing.</exception>
    public static SherpaSpeechSynthesizer Load(string modelsRoot, SpeechVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);

        var dir = SpeechModelLocator.VoiceDirectory(modelsRoot, voice);
        var config = new OfflineTtsConfig();
        config.Model.NumThreads = 2;
        config.Model.Debug = 0;

        // NOTE: OfflineTtsConfig is a struct. These take it by ref on purpose -- passing it by
        // value would fill in a copy, leave the real config's model paths empty, and sherpa-onnx
        // then fails silently and returns no audio rather than reporting a missing model.
        if (voice.Engine == SpeechEngine.Supertonic)
        {
            ConfigureSupertonic(ref config, dir);
        }
        else
        {
            ConfigureVits(ref config, dir, SpeechModelLocator.EspeakDataDirectory(modelsRoot));
        }

        return new SherpaSpeechSynthesizer(new OfflineTts(config), voice);
    }

    public SpeechAudio Synthesize(string text, float speed = 1.0f)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var generation = new OfflineTtsGenerationConfig
        {
            Sid = Voice.SpeakerId,
            Speed = speed * Voice.DefaultSpeed,
        };

        // Supertonic is multilingual and needs telling; a Piper voice speaks one language by
        // construction and the key is ignored, so it costs nothing to set it either way.
        generation.Extra["lang"] = Voice.Language;

        var audio = _tts.GenerateWithConfig(text, generation, null);

        // A native failure comes back as a null handle, and the binding then throws a bare
        // NullReferenceException out of its Samples getter -- which says nothing about which voice
        // or which sentence. Name both, because that is the whole diagnosis.
        float[] samples;
        try
        {
            samples = audio.Samples;
        }
        catch (NullReferenceException ex)
        {
            throw new InvalidOperationException(
                $"sherpa-onnx produced no audio for voice '{Voice.Id}' (sid {Voice.SpeakerId}, "
                + $"speed {generation.Speed:0.##}, lang '{Voice.Language}'): \"{text}\"",
                ex);
        }

        return new SpeechAudio(samples, audio.SampleRate);
    }

    public void Dispose() => _tts.Dispose();

    private static void ConfigureVits(
        ref OfflineTtsConfig config,
        string voiceDirectory,
        string espeakData)
    {
        var model = SpeechModelLocator.FindVitsModel(voiceDirectory)
            ?? throw new FileNotFoundException($"No .onnx model in '{voiceDirectory}'.", voiceDirectory);

        config.Model.Vits.Model = model;
        config.Model.Vits.Tokens = Require(Path.Join(voiceDirectory, "tokens.txt"));
        config.Model.Vits.DataDir = Require(espeakData);
    }

    private static void ConfigureSupertonic(ref OfflineTtsConfig config, string voiceDirectory)
    {
        string At(string name) => Require(Path.Join(voiceDirectory, name));

        config.Model.Supertonic.DurationPredictor = At("duration_predictor.int8.onnx");
        config.Model.Supertonic.TextEncoder = At("text_encoder.int8.onnx");
        config.Model.Supertonic.VectorEstimator = At("vector_estimator.int8.onnx");
        config.Model.Supertonic.Vocoder = At("vocoder.int8.onnx");
        config.Model.Supertonic.TtsJson = At("tts.json");
        config.Model.Supertonic.UnicodeIndexer = At("unicode_indexer.bin");
        config.Model.Supertonic.VoiceStyle = At("voice.bin");
    }

    // sherpa-onnx reports a missing file as a bare "does not exist" on stderr and then produces
    // silence, so the managed side checks first and says which file and which voice.
    private static string Require(string path) =>
        File.Exists(path) || Directory.Exists(path)
            ? path
            : throw new FileNotFoundException($"Sprachmodell-Datei fehlt: '{path}'.", path);
}
