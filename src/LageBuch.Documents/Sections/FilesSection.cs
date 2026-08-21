using LageBuch.Domain.Files;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents.Sections;

/// <summary>
/// Lists every attached file (image or PDF) by its display name, then renders attached images
/// inline below the list. A PDF attachment's pages are appended after the whole report instead
/// (see <see cref="PdfAttachmentMerger"/>) and carry no caption of their own — this list is what
/// makes a PDF attachment's name visible anywhere in the export at all.
/// </summary>
public static class FilesSection
{
    public static void Compose(IContainer container, IReadOnlyList<IncidentFile> files, IReadOnlyDictionary<Guid, byte[]> imageBytesById)
    {
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text("Angehängte Dateien").FontSize(14).SemiBold().FontColor(Colors.Blue.Darken1);

            if (files.Count == 0)
            {
                column.Item().Text("— keine Einträge —").Italic().FontColor(Colors.Grey.Medium);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3); // Name
                    columns.RelativeColumn(2); // Hinzugefügt von
                    columns.RelativeColumn(2); // Datum
                });

                table.Header(header =>
                {
                    foreach (var title in new[] { "Name", "Hinzugefügt von", "Datum" })
                        header.Cell().Element(Cells.Header).Text(title).SemiBold();
                });

                foreach (var file in files)
                {
                    table.Cell().Element(Cells.Body).Text(file.DisplayName);
                    table.Cell().Element(Cells.Body).Text(file.AddedBy);
                    table.Cell().Element(Cells.Body).Text(Formatting.Timestamp(file.AddedAt));
                }
            });

            foreach (var file in files)
            {
                if (!imageBytesById.TryGetValue(file.Id, out var bytes))
                    continue;
                column.Item().PaddingTop(6).Text(file.DisplayName).SemiBold().FontSize(9);
                column.Item().MaxHeight(400).Image(bytes).FitArea();
            }
        });
    }
}
