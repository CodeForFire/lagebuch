using System.Runtime.CompilerServices;

namespace LageBuch.Documents.Tests;

// The export registers no fonts of its own, so every page renders in QuestPDF's
// bundled Lato. A character Lato lacks used to be borrowed from whatever font was
// installed on the rendering machine: it looked right on a developer box and came
// out blank on a slim container or on Android, with nothing in the logs either way.
// Pinning the suite to Lato alone turns that silent, host-dependent defect into a
// failing test -- see the "Legende" swatches and the task checkmark this replaced.
//
// Both settings are looked up by name because QuestPDF 2026.9 renamed them
// (UseEnvironmentFonts -> UseSystemFonts, CheckIfAllTextGlyphsAreAvailable ->
// ThrowOnMissingTextGlyphs) and kept the old names as [Obsolete], which this repo
// builds as an error. Naming either spelling directly would break the build on one
// side of that version boundary. Once the bump has landed this can collapse into
// two plain property assignments.
internal static class LatoOnlyFontEnvironment
{
    [ModuleInitializer]
    internal static void Pin()
    {
        Set(current: "UseSystemFonts", legacy: "UseEnvironmentFonts", value: false);
        Set(current: "ThrowOnMissingTextGlyphs", legacy: "CheckIfAllTextGlyphsAreAvailable", value: true);
    }

    private static void Set(string current, string legacy, bool value)
    {
        var property = typeof(QuestPDF.Settings).GetProperty(current)
            ?? typeof(QuestPDF.Settings).GetProperty(legacy)
            ?? throw new InvalidOperationException(
                $"QuestPDF.Settings exposes neither '{current}' nor '{legacy}'.");

        property.SetValue(obj: null, value);
    }
}
