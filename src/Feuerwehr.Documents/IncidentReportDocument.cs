using Feuerwehr.Documents.Sections;
using Feuerwehr.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Feuerwehr.Documents;

public sealed class IncidentReportDocument : IDocument
{
    private readonly Incident _incident;

    public IncidentReportDocument(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incident = incident;
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
