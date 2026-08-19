using Feuerwehr.Documents.Sections;
using Feuerwehr.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents;

public sealed class IncidentReportDocument : IDocument
{
    private readonly Incident _incident;
    private readonly IReadOnlyList<(string FileName, byte[] Bytes)> _images;

    /// <param name="incident">The incident to render.</param>
    /// <param name="imageBytes">
    /// Bytes for the image-typed entries in <see cref="Incident.Files"/>, keyed by
    /// <c>IncidentFile.Id</c> — resolved by the caller (this project stays filesystem-free). Missing
    /// or non-image entries are simply not shown, rather than failing the export.
    /// </param>
    public IncidentReportDocument(Incident incident, IReadOnlyDictionary<Guid, byte[]>? imageBytes = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incident = incident;
        _images = incident.Files
            .Where(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .Where(f => imageBytes is not null && imageBytes.ContainsKey(f.Id))
            .Select(f => (f.FileName, imageBytes![f.Id]))
            .ToList();
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer document)
    {
        PdfLicense.Ensure();

        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontSize(10));

            page.Header().Element(c => IncidentHeaderSection.Compose(c, _incident));

            page.Content().PaddingVertical(10).Column(column =>
            {
                column.Spacing(14);
                column.Item().Element(c => ChecklistSection.Compose(c, _incident));
                column.Item().Element(c => EtbSection.Compose(c, _incident));
                column.Item().Element(c => RolesSection.Compose(c, _incident));
                column.Item().Element(c => ForcesSection.Compose(c, _incident));
                column.Item().Element(c => AtemschutzSection.Compose(c, _incident));
                column.Item().Element(c => FilesSection.Compose(c, _images));
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("Seite ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
    }
}
