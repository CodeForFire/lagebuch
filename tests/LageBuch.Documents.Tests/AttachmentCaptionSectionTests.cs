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

    [Fact]
    public void GeneratePdf_bears_the_display_name_not_the_file_name()
    {
        // The caption must echo the Files-list row's label, including a renamed one — the only
        // input that differs is DisplayName (FileName stays "bericht.pdf" in both), so if the bytes
        // change the label is what drove it. (A size-delta proxy is unusable here: QuestPDF compresses
        // its content streams, so longer names do not reliably enlarge the output.)
        var defaultLabel = AttachmentCaptionSection.GeneratePdf(File("bericht.pdf"));
        var renamedLabel = AttachmentCaptionSection.GeneratePdf(File("Lagebericht Erdgeschoss"));

        Assert.NotEqual(defaultLabel, renamedLabel);
    }
}