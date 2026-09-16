using LageBuch.Domain;
using LageBuch.Domain.Files;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>
/// Renders the single-page caption that <see cref="PdfAttachmentMerger"/> inserts directly before an
/// attached PDF's pages in the exported report. It echoes that file's row in the "Angehängte
/// Dateien" list (<see cref="FilesSection"/>) — display name, added-by, date — so an appendix can be
/// visually linked back to its entry in the file table.
/// </summary>
public static class AttachmentCaptionSection
{
    public static byte[] GeneratePdf(IncidentFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        PdfLicense.Ensure();
        return new AttachmentCaptionDocument(file).GeneratePdf();
    }

    private sealed class AttachmentCaptionDocument(IncidentFile file) : IDocument
    {
        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    column.Item().Text("Angehängte Datei").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);
                    column.Item().PaddingBottom(2).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                    column.Item().Text(file.DisplayName).FontSize(16).Bold();
                    column.Item().Text(text =>
                    {
                        text.Span("Hinzugefügt von: ").SemiBold();
                        text.Span(file.AddedBy);
                    });
                    column.Item().Text(text =>
                    {
                        text.Span("Datum: ").SemiBold();
                        text.Span(Formatting.Timestamp(file.AddedAt));
                    });
                    column.Item().PaddingTop(6)
                        .Text("Die folgenden Seiten gehören zu dieser angehängten Datei.")
                        .Italic()
                        .FontColor(Colors.Grey.Darken1);
                });
            });
        }
    }
}