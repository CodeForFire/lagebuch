using LageBuch.Domain;
using QuestPDF.Fluent;

namespace LageBuch.Documents;

public static class IncidentPdf
{
    /// <param name="incident">The incident to render.</param>
    /// <param name="fileBytes">
    /// Bytes for image entries in <see cref="Incident.Files"/>, keyed by id — resolved by the caller
    /// (this project stays filesystem-free). Image entries render inline via
    /// <see cref="Sections.FilesSection"/>. An entry with no bytes supplied (a missing sibling-folder
    /// file) is skipped rather than failing the export.
    /// </param>
    /// <param name="pdfAttachmentPaths">
    /// Disk paths for <c>application/pdf</c> entries in <see cref="Incident.Files"/>, keyed by id —
    /// appended as extra pages via <see cref="PdfAttachmentMerger"/>, which merges straight from
    /// these paths rather than requiring the caller to load each PDF's bytes into memory first (see
    /// issue #167 P1 #3). An entry with no path supplied is skipped rather than failing the export.
    /// </param>
    /// <param name="sections">
    /// Which of the 8 body sections to include (#262); defaults to all of them. Deselecting
    /// <see cref="IncidentPdfSections.Files"/> also skips the merged PDF-attachment pages below,
    /// since those pages are conceptually part of "Angehängte Dateien".
    /// </param>
    public static byte[] Generate(
        Incident incident,
        IReadOnlyDictionary<Guid, byte[]>? fileBytes = null,
        IReadOnlyDictionary<Guid, string>? pdfAttachmentPaths = null,
        IncidentPdfSections sections = IncidentPdfSections.All)
    {
        ArgumentNullException.ThrowIfNull(incident);
        PdfLicense.Ensure();
        fileBytes ??= new Dictionary<Guid, byte[]>();
        pdfAttachmentPaths ??= new Dictionary<Guid, string>();

        var baseReport = new IncidentReportDocument(incident, fileBytes, sections).GeneratePdf();

        if (!sections.HasFlag(IncidentPdfSections.Files))
        {
            return baseReport;
        }

        var pdfAttachments = incident.Files
            .Where(f => f.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            .Where(f => pdfAttachmentPaths.ContainsKey(f.Id))
            .Select(f => pdfAttachmentPaths[f.Id])
            .ToList();

        return pdfAttachments.Count == 0 ? baseReport : PdfAttachmentMerger.Append(baseReport, pdfAttachments);
    }
}
