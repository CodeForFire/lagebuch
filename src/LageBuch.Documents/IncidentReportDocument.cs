using LageBuch.Documents.Sections;
using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents;

public sealed class IncidentReportDocument : IDocument
{
    private readonly Incident _incident;
    private readonly IReadOnlyDictionary<Guid, byte[]> _imageBytesById;
    private readonly IReadOnlyDictionary<Guid, byte[]> _routeOverviewPngById;

    /// <param name="incident">The incident to render.</param>
    /// <param name="fileBytes">
    /// Bytes for entries in <see cref="Incident.Files"/>, keyed by <c>IncidentFile.Id</c> —
    /// resolved by the caller (this project stays filesystem-free). Every attached file is listed
    /// by name regardless of whether bytes were supplied; only image entries with bytes present
    /// are additionally rendered inline (see <see cref="Sections.FilesSection"/>).
    /// </param>
    /// <param name="routeOverviewPngById">See <see cref="IncidentPdf.Generate"/>.</param>
    public IncidentReportDocument(
        Incident incident,
        IReadOnlyDictionary<Guid, byte[]>? fileBytes = null,
        IReadOnlyDictionary<Guid, byte[]>? routeOverviewPngById = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incident = incident;
        _imageBytesById = incident.Files
            .Where(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .Where(f => fileBytes is not null && fileBytes.ContainsKey(f.Id))
            .ToDictionary(f => f.Id, f => fileBytes![f.Id]);
        _routeOverviewPngById = routeOverviewPngById ?? new Dictionary<Guid, byte[]>();
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
                column.Item().Element(c => TasksSection.Compose(c, _incident));
                column.Item().Element(c => WasserfoerderungSection.Compose(c, _incident, _routeOverviewPngById));
                column.Item().Element(c => AtemschutzSection.Compose(c, _incident));
                column.Item().Element(c => CoMessprotokollSection.Compose(c, _incident));
                column.Item().Element(c => FilesSection.Compose(c, _incident.Files, _imageBytesById));
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
