using QuestPDF.Fluent;

namespace LageBuch.Documents;

/// <summary>
/// Appends attached PDF files as extra pages after a generated report. Kept separate from
/// <see cref="IncidentPdf"/> so the merge step — the one part of PDF export that touches the
/// filesystem — is independently testable.
/// </summary>
public static class PdfAttachmentMerger
{
    /// <summary>
    /// Appends every attachment's pages, in order, after <paramref name="baseReport"/>'s own pages.
    /// QuestPDF's <c>DocumentOperation</c> works on file paths only (no in-memory overload), so both
    /// the base report and each attachment are round-tripped through a temp file.
    /// </summary>
    public static byte[] Append(byte[] baseReport, IReadOnlyList<byte[]> pdfAttachments)
    {
        ArgumentNullException.ThrowIfNull(baseReport);
        ArgumentNullException.ThrowIfNull(pdfAttachments);
        if (pdfAttachments.Count == 0)
            return baseReport;

        var workDir = Path.Combine(Path.GetTempPath(), $"lagebuch-pdf-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var basePath = Path.Combine(workDir, "report.pdf");
            File.WriteAllBytes(basePath, baseReport);

            var operation = DocumentOperation.LoadFile(basePath, password: null);
            for (var i = 0; i < pdfAttachments.Count; i++)
            {
                var attachmentPath = Path.Combine(workDir, $"attachment-{i}.pdf");
                File.WriteAllBytes(attachmentPath, pdfAttachments[i]);
                operation = operation.MergeFile(attachmentPath, pageSelector: null);
            }

            var outPath = Path.Combine(workDir, "merged.pdf");
            operation.Save(outPath);
            return File.ReadAllBytes(outPath);
        }
        finally
        {
            Directory.Delete(workDir, recursive: true);
        }
    }
}
