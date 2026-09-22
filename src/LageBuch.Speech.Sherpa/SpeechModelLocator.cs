using System.Diagnostics.CodeAnalysis;

namespace LageBuch.Speech.Sherpa;

/// <summary>
/// Finds the <c>speech-models</c> directory and the files of one voice inside it.
/// </summary>
/// <remarks>
/// <para>
/// The models ship as loose files next to the executable rather than inside the single-file bundle,
/// so the app resolves them from <see cref="AppContext.BaseDirectory"/> -- which, for .NET 6 and
/// later single-file publishes, is the executable's own directory.
/// </para>
/// <para>
/// <b>Why the ASCII check.</b> sherpa-onnx's C# config structs marshal every path as
/// <c>UnmanagedType.LPStr</c>, which is ANSI, not UTF-8. A model path containing an umlaut is
/// handed to the native library mangled, and it fails with a confusing "file not found" on a file
/// that plainly exists. Install paths are ASCII in practice, but a developer running from a
/// checkout under, say, <c>/home/müller/</c> would hit it immediately -- so the locator detects it
/// and reports it as an unavailable engine with a reason, rather than letting it surface as a
/// mystery.
/// </para>
/// </remarks>
public static class SpeechModelLocator
{
    /// <summary>Directory name holding the voices, both in the publish output and in a checkout.</summary>
    public const string ModelsDirectoryName = "speech-models";

    // How far above the binary to look. A published install has the models beside the executable
    // and never walks at all; a development run sits in bin/Debug/net10.0 and needs about six
    // levels to reach the repository root. Bounded on purpose -- an unbounded walk in a published
    // app could wander into an unrelated "speech-models" somewhere above the install directory.
    private const int MaxLevelsAbove = 8;

    /// <summary>The espeak-ng data shared by every Piper voice, pruned to German.</summary>
    public const string EspeakDataDirectoryName = "espeak-ng-data";

    /// <summary>
    /// Resolves the models root, or explains why speech is unavailable.
    /// </summary>
    /// <param name="root">The resolved directory, when this returns true.</param>
    /// <param name="reason">A German, user-facing explanation, when this returns false.</param>
    /// <param name="baseDirectory">Overridable for tests; defaults to the app's own directory.</param>
    public static bool TryLocate(
        [NotNullWhen(true)] out string? root,
        [NotNullWhen(false)] out string? reason,
        string? baseDirectory = null)
    {
        root = null;
        var start = baseDirectory ?? AppContext.BaseDirectory;
        var candidate = Find(start);

        if (candidate is null)
        {
            reason = $"Keine Sprachmodelle gefunden (gesucht ab {start}).";
            return false;
        }

        if (!IsAscii(candidate))
        {
            reason =
                $"Der Pfad zu den Sprachmodellen enthält Sonderzeichen ({candidate}). "
                + "Die Sprachausgabe benötigt einen Pfad ohne Umlaute.";
            return false;
        }

        root = candidate;
        reason = null;
        return true;
    }

    /// <summary>The directory holding one voice's files.</summary>
    public static string VoiceDirectory(string root, SpeechVoice voice)
    {
        ArgumentNullException.ThrowIfNull(voice);
        return Path.Join(root, voice.DirectoryName);
    }

    /// <summary>The shared, German-only espeak-ng data directory.</summary>
    public static string EspeakDataDirectory(string root) =>
        Path.Join(root, EspeakDataDirectoryName);

    /// <summary>
    /// The single <c>.onnx</c> of a Piper voice. Their file names carry the upstream voice name
    /// (<c>de_DE-thorsten-medium.onnx</c>), which is not our voice id, so it is discovered rather
    /// than constructed.
    /// </summary>
    public static string? FindVitsModel(string voiceDirectory) =>
        Directory.Exists(voiceDirectory)
            ? Directory.EnumerateFiles(voiceDirectory, "*.onnx").Order(StringComparer.Ordinal).FirstOrDefault()
            : null;

    /// <summary>
    /// The models directory beside <paramref name="start"/>, or above it, or null.
    /// </summary>
    /// <remarks>
    /// <b>This is the only resolver.</b> The audition harness used to carry its own copy that
    /// walked up the tree while this one did not, so the harness always found the voices and the
    /// app never could -- every test and every rendered audition passed while nothing spoke. Two
    /// resolvers for one question was the real defect; the wrong path was only its symptom.
    /// </remarks>
    private static string? Find(string start)
    {
        var dir = new DirectoryInfo(start);

        for (var level = 0; dir is not null && level <= MaxLevelsAbove; level++)
        {
            var candidate = Path.Join(dir.FullName, ModelsDirectoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool IsAscii(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAscii(c))
            {
                return false;
            }
        }

        return true;
    }
}
