using LageBuch.Documents.Sections;
using LageBuch.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LageBuch.Documents;

public sealed class IncidentReportDocument : IDocument
{
    private readonly Incident _incident;
    private readonly DateTimeOffset _asOf;
    private readonly IReadOnlyDictionary<Guid, byte[]> _imageBytesById;
    private readonly IncidentPdfSections _sections;

    /// <param name="incident">The incident to render.</param>
    /// <param name="fileBytes">
    /// Bytes for entries in <see cref="Incident.Files"/>, keyed by <c>IncidentFile.Id</c> —
    /// resolved by the caller (this project stays filesystem-free). Every attached file is listed
    /// by name regardless of whether bytes were supplied; only image entries with bytes present
    /// are additionally rendered inline (see <see cref="Sections.FilesSection"/>).
    /// </param>
    /// <param name="sections">
    /// Which of the 8 body sections to render (#262); defaults to all of them. The header and
    /// footer are always rendered regardless of this selection.
    /// </param>
    /// <param name="asOf">
    /// The moment the export was taken. <see cref="Sections.TasksSection"/> uses it to decide which
    /// Aufgaben are overdue, and the footer of a still-open incident prints it as the Zwischenstand
    /// time (see <see cref="InterimMarker"/>); every other section is a pure function of the incident.
    /// </param>
    public IncidentReportDocument(Incident incident, DateTimeOffset asOf, IReadOnlyDictionary<Guid, byte[]>? fileBytes = null, IncidentPdfSections sections = IncidentPdfSections.All)
    {
        ArgumentNullException.ThrowIfNull(incident);
        _incident = incident;
        _asOf = asOf;
        _sections = sections;

        // Skipped entirely when Files is deselected -- FilesSection.Compose (the only consumer)
        // won't run below, so copying every attached image's bytes here would be wasted work.
        _imageBytesById = sections.HasFlag(IncidentPdfSections.Files)
            ? incident.Files
                .Where(f => f.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                .Where(f => fileBytes is not null && fileBytes.ContainsKey(f.Id))
                .ToDictionary(f => f.Id, f => fileBytes![f.Id])
            : new Dictionary<Guid, byte[]>();
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
                if (_sections.HasFlag(IncidentPdfSections.Checklist))
                {
                    column.Item().Element(c => ChecklistSection.Compose(c, _incident));
                }

                if (_sections.HasFlag(IncidentPdfSections.Etb))
                {
                    column.Item().Element(c => EtbSection.Compose(c, _incident));
                }

                if (_sections.HasFlag(IncidentPdfSections.Roles))
                {
                    column.Item().Element(c => RolesSection.Compose(c, _incident));
                }

                if (_sections.HasFlag(IncidentPdfSections.Forces))
                {
                    column.Item().Element(c => ForcesSection.Compose(c, _incident));
                }

                if (_sections.HasFlag(IncidentPdfSections.Tasks))
                {
                    column.Item().Element(c => TasksSection.Compose(c, _incident, _asOf));
                }

                if (_sections.HasFlag(IncidentPdfSections.Atemschutz))
                {
                    column.Item().Element(c => AtemschutzSection.Compose(c, _incident));
                }

                if (_sections.HasFlag(IncidentPdfSections.CoMessprotokoll))
                {
                    column.Item().Element(c => CoMessprotokollSection.Compose(c, _incident));
                }

                if (_sections.HasFlag(IncidentPdfSections.Files))
                {
                    column.Item().Element(c => FilesSection.Compose(c, _incident.Files, _imageBytesById));
                }
            });

            var interimMarker = InterimMarker(_incident, _asOf);
            page.Footer().Layers(layers =>
            {
                // Layered rather than split into columns, so the page count stays centred and the
                // marker still gets the full width to the left of it instead of wrapping.
                layers.PrimaryLayer().AlignCenter().Text(t =>
                {
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
                if (interimMarker is not null)
                {
                    layers.Layer().AlignLeft().Text(interimMarker);
                }
            });
        });
    }

    /// <summary>
    /// The footer line on every page of a still-open incident's report (#465), or <c>null</c> once
    /// it is closed. A PDF taken mid-Einsatz — on the host or on a joined client — is a snapshot
    /// that will be overtaken, and has to say so on paper where "Status: Offen" is easy to miss.
    /// </summary>
    public static string? InterimMarker(Incident incident, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(incident);
        return incident.State == IncidentState.Open
            ? $"Zwischenstand – Stand {Formatting.Timestamp(asOf)}"
            : null;
    }
}
