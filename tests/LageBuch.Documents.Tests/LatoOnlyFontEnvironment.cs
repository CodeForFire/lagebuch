using System.Runtime.CompilerServices;

namespace LageBuch.Documents.Tests;

// The export registers no fonts of its own, so every page renders in QuestPDF's
// bundled Lato. A character Lato lacks used to be borrowed from whatever font was
// installed on the rendering machine: it looked right on a developer box and came
// out blank on a slim container or on Android, with nothing in the logs either way.
// Pinning the suite to Lato alone turns that silent, host-dependent defect into a
// failing test -- see the "Legende" swatches and the task checkmark this replaced.
//
// Production deliberately does not throw (PdfLicense.Ensure), because free text the
// operator types must never cost them the export. That rule is for text LageBuch
// itself writes, which is why it lives here and not there.
internal static class LatoOnlyFontEnvironment
{
    [ModuleInitializer]
    internal static void Pin()
    {
        // Ensure() pins the production font settings, and it is idempotent -- so it has
        // to run here rather than later from the first Generate call, which would reset
        // ThrowOnMissingTextGlyphs underneath us and quietly disable this whole guard.
        PdfLicense.Ensure();

        QuestPDF.Settings.UseSystemFonts = false;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = true;
    }
}
