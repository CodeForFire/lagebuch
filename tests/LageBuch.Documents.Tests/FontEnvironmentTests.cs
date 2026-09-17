using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// LatoOnlyFontEnvironment and PdfLicense.Ensure want opposite things from
// ThrowOnMissingTextGlyphs, and both write the same global. If Ensure ran last the
// suite would keep passing while guarding nothing at all -- every other test here
// leans on the throw to notice an unrenderable character. These pin the outcome of
// that race so it cannot silently flip.
public class FontEnvironmentTests
{
    private const string CheckRelaxed =
        "PdfLicense.Ensure() ran after the module initializer and turned the glyph check off; "
        + "this suite is no longer catching characters the bundled font lacks.";

    private const string SystemFontsVisible =
        "Fonts installed on this machine are visible to the renderer, so a missing glyph "
        + "can be masked here and still be missing on another machine.";

    [Fact]
    public void Rendering_a_document_does_not_relax_the_glyph_check()
    {
        // Generate() calls PdfLicense.Ensure(), which sets ThrowOnMissingTextGlyphs=false
        // for production. The module initializer must already have spent that one-shot.
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2)));
        IncidentPdf.Generate(
            Incident.Start(clock, new SessionOperator("Müller"), "Brand"),
            clock.Now,
            new Dictionary<Guid, byte[]>());

        Assert.True(QuestPDF.Settings.ThrowOnMissingTextGlyphs, CheckRelaxed);
    }

    [Fact]
    public void The_suite_renders_in_the_bundled_font_alone()
    {
        Assert.False(QuestPDF.Settings.UseSystemFonts, SystemFontsVisible);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now => now;
    }
}
