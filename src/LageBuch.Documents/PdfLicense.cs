using QuestPDF.Infrastructure;

namespace LageBuch.Documents;

/// <summary>
/// Configures QuestPDF for this application: the license, and the font environment
/// every export renders in. Community license is free and valid for open-source
/// projects and small organisations. Idempotent and thread-safe.
/// </summary>
public static class PdfLicense
{
    private static int _configured;

    public static void Ensure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 0)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            // The export ships its own font and renders in it alone: what a PDF looks like
            // must not depend on which fonts the machine writing it happens to have.
            QuestPDF.Settings.UseSystemFonts = false;

            // ...but a character that font lacks must never cost the operator the whole
            // export. Free text reaches these pages — ETB entries, task titles, attachment
            // names — and QuestPDF throws on an unrenderable glyph by default. A blank where
            // an emoji was is survivable mid-Einsatz; losing the document is not. The text
            // LageBuch itself writes is held to a stricter rule by the tests, which flip this
            // back on: see LatoOnlyFontEnvironment in LageBuch.Documents.Tests.
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        }
    }
}
