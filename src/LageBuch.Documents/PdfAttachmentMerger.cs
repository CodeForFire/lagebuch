using LageBuch.Documents.Sections;
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
    /// Appends every attachment's pages, in order, after <paramref name="baseReport"/>'s own pages,
    /// preceding each attachment with a single caption page (see <see cref="AttachmentCaptionSection"/>)
    /// that links its pages back to the matching row in the report's "Angehängte Dateien" list.
    /// QuestPDF's <c>DocumentOperation</c> works on file paths only (no in-memory overload), so the
    /// base report and each generated caption page are round-tripped through a temp file, but
    /// attachments are merged straight from <paramref name="attachments"/>' paths — every attachment
    /// already lives on disk (see issue #167 P1 #3), so there is no need to load it into memory and
    /// write it back out to a temp copy first.
    /// </summary>
    public static byte[] Append(byte[] baseReport, IReadOnlyList<PdfAttachmentToMerge> attachments)
    {
        ArgumentNullException.ThrowIfNull(baseReport);
        ArgumentNullException.ThrowIfNull(attachments);
        if (attachments.Count == 0)
        {
            return baseReport;
        }

        foreach (var attachment in attachments)
        {
            if (!File.Exists(attachment.Path))
            {
                throw new FileNotFoundException($"PDF-Anhang nicht gefunden: {attachment.Path}", attachment.Path);
            }
        }

        var workDir = Path.Combine(Path.GetTempPath(), $"lagebuch-pdf-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var basePath = Path.Combine(workDir, "report.pdf");
            File.WriteAllBytes(basePath, baseReport);

            var operation = DocumentOperation.LoadFile(basePath, password: null);
            for (var i = 0; i < attachments.Count; i++)
            {
                var attachment = attachments[i];

                var captionPath = Path.Combine(workDir, $"caption-{i}.pdf");
                File.WriteAllBytes(captionPath, AttachmentCaptionSection.GeneratePdf(attachment.File));

                operation = operation.MergeFile(captionPath, pageSelector: null);
                operation = operation.MergeFile(attachment.Path, pageSelector: null);
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
