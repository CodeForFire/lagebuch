using LageBuch.Documents.Sections;
using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents;

public sealed class IncidentReportDocument : IDocument
{
    private readonly Incident _incident;
    private readonly IReadOnlyDictionary<Guid, string> _imagePathsById;

    /// <param name="incident">The incident to render.</param>
    /// <param name="filePaths">
    /// Disk paths for entries in <see cref="Incident.Files"/>, keyed by <c>IncidentFile.Id</c> —
    /// resolved by the caller (this project stays filesystem-free). Every attached file is listed
    /// by name regardless of whether a path was supplied; only image entries with a path present
    /// are additionally rendered inline (see <see cref="Sections.FilesSection"/>).
    /// </param>
    public IncidentReportDocument(Incident incident, IReadOnlyDictionary<Guid, string>? filePaths = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incident = incident;
        _imagePathsById = incident.Files
            .Where(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .Where(f => filePaths is not null && filePaths.ContainsKey(f.Id))
            .ToDictionary(f => f.Id, f => filePaths![f.Id]);
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

    public void Compose(IDocumentContainer container)
    {
        PdfLicense.Ensure();

        container.Page(page =>
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
                column.Item().Element(c => TasksSection.Compose(c, _incident));
                column.Item().Element(c => AtemschutzSection.Compose(c, _incident));
                column.Item().Element(c => CoMessprotokollSection.Compose(c, _incident));
                column.Item().Element(c => FilesSection.Compose(c, _incident.Files, _imagePathsById));
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
