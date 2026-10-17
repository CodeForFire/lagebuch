using LageBuch.Documents.Sections;
using LageBuch.Domain.Files;

namespace LageBuch.Documents.Tests;

public class AttachmentCaptionSectionTests
{
    private static readonly DateTimeOffset AddedAt = new(2026, 9, 16, 8, 30, 0, TimeSpan.FromHours(2));

    private static IncidentFile File(string displayName) =>
        IncidentFile
            .Create("bericht.pdf", "application/pdf", 100, AddedAt, "Müller")
            .WithDisplayName(displayName);

    [Fact]
    public void GeneratePdf_produces_a_valid_single_page_pdf()
    {
        var bytes = AttachmentCaptionSection.GeneratePdf(File("Lagebericht Erdgeschoss"));

        PdfAssert.IsPdf(bytes);
        Assert.Equal(1, PdfAssert.CountPages(bytes));
    }

    // The caption echoes the file's row in the Angehängte-Dateien list -- display name, added-by,
    // date -- so an appendix can be traced back to its entry.
    [Fact]
    public void The_caption_echoes_the_files_list_row()
    {
        var text = PdfText.Of(AttachmentCaptionSection.GeneratePdf(File("Lagebericht Erdgeschoss")));

        PdfText.AssertInOrder(
            text,
            "Angehängte Datei",
            "Lagebericht Erdgeschoss",
            "Hinzugefügt von: Müller",
            "Datum: 16.09.2026 08:30",
            "Die folgenden Seiten gehören zu dieser angehängten Datei.");
    }

    [Fact]
    public void The_caption_bears_the_display_name_not_the_file_name()
    {
        var text = PdfText.Of(AttachmentCaptionSection.GeneratePdf(File("Lagebericht Erdgeschoss")));

        Assert.Contains("Lagebericht Erdgeschoss", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bericht.pdf", text, StringComparison.Ordinal);
    }
}
