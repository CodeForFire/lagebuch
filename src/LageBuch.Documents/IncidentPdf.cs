using LageBuch.Domain;
using QuestPDF.Fluent;

namespace LageBuch.Documents;

public static class IncidentPdf
{
    /// <param name="incident">The incident to render.</param>
    /// <param name="filePaths">
    /// Disk paths for entries in <see cref="Incident.Files"/>, keyed by id — resolved by the caller
    /// (this project stays filesystem-free, per issue #167 P1: nothing here loads a whole attachment
    /// into memory). Image entries render inline via <see cref="Sections.FilesSection"/>;
    /// <c>application/pdf</c> entries are appended as extra pages via <see cref="PdfAttachmentMerger"/>,
    /// which merges straight from disk. An entry with no path supplied (a missing sibling-folder
    /// file) is skipped rather than failing the export.
    /// </param>
    public static byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, string>? filePaths = null)
    {
        ArgumentNullException.ThrowIfNull(incident);
        PdfLicense.Ensure();
        filePaths ??= new Dictionary<Guid, string>();

        var baseReport = new IncidentReportDocument(incident, filePaths).GeneratePdf();

        var pdfAttachments = incident.Files
            .Where(f => f.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
            .Where(f => filePaths.ContainsKey(f.Id))
            .Select(f => filePaths[f.Id])
            .ToList();

        return pdfAttachments.Count == 0 ? baseReport : PdfAttachmentMerger.Append(baseReport, pdfAttachments);
    }
}
