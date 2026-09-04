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
    /// QuestPDF's <c>DocumentOperation</c> works on file paths only (no in-memory overload), so the
    /// base report is round-tripped through a temp file, but attachments are merged straight from
    /// <paramref name="pdfAttachmentPaths"/> — every attachment already lives on disk (see issue
    /// #167 P1 #3), so there is no need to load it into memory and write it back out to a temp copy
    /// first.
    /// </summary>
    public static byte[] Append(byte[] baseReport, IReadOnlyList<string> pdfAttachmentPaths)
    {
        ArgumentNullException.ThrowIfNull(baseReport);
        ArgumentNullException.ThrowIfNull(pdfAttachmentPaths);
        if (pdfAttachmentPaths.Count == 0)
        {
            return baseReport;
        }

        foreach (var path in pdfAttachmentPaths)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"PDF-Anhang nicht gefunden: {path}", path);
            }
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"lagebuch-pdf-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var basePath = Path.Combine(workDir, "report.pdf");
            File.WriteAllBytes(basePath, baseReport);

            var operation = DocumentOperation.LoadFile(basePath, password: null);
            foreach (var attachmentPath in pdfAttachmentPaths)
            {
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
