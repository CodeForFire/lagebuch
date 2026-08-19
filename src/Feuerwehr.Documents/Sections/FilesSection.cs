using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents.Sections;

/// <summary>
/// Renders attached images inline, flowing into the report like any other section. Attached PDFs
/// cannot flow into this <c>Column</c> layout — they are appended as extra pages after the whole
/// report instead (see <see cref="PdfAttachmentMerger"/>), so this section covers images only.
/// </summary>
public static class FilesSection
{
    public static void Compose(IContainer container, IReadOnlyList<(string FileName, byte[] Bytes)> images)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Angehängte Bilder").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (images.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            foreach (var (fileName, bytes) in images)
            {
                column.Item().Text(fileName).SemiBold().FontSize(9);
                column.Item().MaxHeight(400).Image(bytes).FitArea();
            }
        });
    }
}
