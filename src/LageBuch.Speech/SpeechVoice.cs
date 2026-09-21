namespace LageBuch.Speech;

/// <summary>Which sherpa-onnx model family a voice belongs to.</summary>
/// <remarks>
/// Only the synthesizer looks at this, when it fills <c>OfflineTtsModelConfig</c>. Everything above
/// <see cref="ISpeechSynthesizer"/> stays engine-agnostic, so swapping the chosen voice after the
/// audition cannot ripple outward.
/// </remarks>
public enum SpeechEngine
{
    /// <summary>A Piper/VITS voice: one .onnx plus tokens.txt, phonemized through espeak-ng.</summary>
    Vits,

    /// <summary>Supertonic 3: four .onnx stages with its own tokenizer, no espeak-ng data.</summary>
    Supertonic,
}

/// <summary>Which voice to offer where a male and a female option are listed.</summary>
public enum SpeechGender
{
    Male,
    Female,
}

/// <summary>
/// A voice the app can speak with, and everything needed to load and to credit it.
/// </summary>
/// <param name="Id">Stable key, also the folder name under the models root and the settings value.</param>
/// <param name="DisplayName">What the Stammdaten voice picker shows.</param>
/// <param name="Gender">Which of the two offered slots this voice fills.</param>
/// <param name="Engine">Which sherpa-onnx model family loads it.</param>
/// <param name="Licence">SPDX-ish licence id for the model weights, for THIRD-PARTY-NOTICES.</param>
/// <param name="Attribution">Dataset/author credit the licence obliges us to carry.</param>
/// <param name="SpeakerId">Speaker index for a multi-speaker model; 0 for single-speaker.</param>
/// <param name="DefaultSpeed">
/// Per-voice speed correction. Supertonic speaks roughly 1.4x slower than Piper at speed 1.0 for
/// the same sentence, measured over four texts, so a shared default would make one engine drawl.
/// </param>
public sealed record SpeechVoice(
    string Id,
    string DisplayName,
    SpeechGender Gender,
    SpeechEngine Engine,
    string Licence,
    string Attribution,
    int SpeakerId = 0,
    float DefaultSpeed = 1.0f)
{
    /// <summary>BCP-47-ish language tag passed to a multilingual engine. Piper voices are German by construction.</summary>
    public string Language { get; init; } = "de";

    /// <summary>Folder under the models root holding this voice's files. Defaults to <see cref="Id"/>.</summary>
    public string DirectoryName { get; init; } = Id;

    /// <summary>True when the licence is not an OSI/FSF-free one and so needs a carve-out in the README.</summary>
    public bool IsRestrictivelyLicensed => Licence.Contains("RAIL", StringComparison.OrdinalIgnoreCase);
}
