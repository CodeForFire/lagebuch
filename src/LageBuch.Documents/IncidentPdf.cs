using LageBuch.Domain;
using QuestPDF.Fluent;

namespace LageBuch.Documents;

public static class IncidentPdf
{
    /// <param name="incident">The incident to render.</param>
    /// <param name="fileBytes">
    /// Bytes for entries in <see cref="Incident.Files"/>, keyed by id — resolved by the caller
    /// (this project stays filesystem-free). Image entries render inline via
    /// <see cref="Sections.FilesSection"/>; <c>application/pdf</c> entries are appended as extra
    /// pages via <see cref="PdfAttachmentMerger"/>. An entry with no bytes supplied (a missing
    /// sibling-folder file) is skipped rather than failing the export.
    /// </param>
    public static byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, byte[]>? fileBytes = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        PdfLicense.Ensure();
        fileBytes ??= new Dictionary<Guid, byte[]>();

        var baseReport = new IncidentReportDocument(incident, fileBytes).GeneratePdf();

        var pdfAttachments = incident.Files
            .Where(f => f.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            .Where(f => fileBytes.ContainsKey(f.Id))
            .Select(f => fileBytes[f.Id])
            .ToList();

        return pdfAttachments.Count == 0 ? baseReport : PdfAttachmentMerger.Append(baseReport, pdfAttachments);
    }
}
