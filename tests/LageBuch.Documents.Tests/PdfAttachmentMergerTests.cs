using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Documents.Tests;

// Exercises QuestPDF's DocumentOperation (backed by the native qpdf library) — needs that native
// dependency available on the machine running the test (present in CI's ubuntu-latest/windows-latest
// legs; some local dev containers may lack it, in which case these fail with a native load error
// distinct from any assertion failure here).
public class PdfAttachmentMergerTests
{
    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    private static byte[] OnePagePdf() =>
        IncidentPdf.Generate(Incident.Start(new Clock(), new SessionOperator("Müller")));

    // Attachments are merged straight from disk paths (issue #167 P1 #3) rather than byte[], since
    // they already live on disk in production — this writes a PDF to a temp file to stand in for
    // that, cleaned up once the test finishes.
    private static string WriteTempPdf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lagebuch-pdf-merger-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Append_with_no_attachments_returns_the_report_unchanged()
    {
        var report = OnePagePdf();

        var result = PdfAttachmentMerger.Append(report, Array.Empty<string>());

        Assert.Same(report, result);
    }

    [Fact]
    public void Append_adds_the_attachments_pages_after_the_reports_own()
    {
        var report = OnePagePdf();
        var reportPages = PdfAssert.CountPages(report);
        var attachment = OnePagePdf(); // any valid PDF stands in for "an attached PDF"
        var attachmentPath = WriteTempPdf(attachment);
        try
        {
            var merged = PdfAttachmentMerger.Append(report, new[] { attachmentPath });

            PdfAssert.IsPdf(merged);
            Assert.Equal(reportPages + PdfAssert.CountPages(attachment), PdfAssert.CountPages(merged));
        }
        finally
        {
            File.Delete(attachmentPath);
        }
    }

    [Fact]
    public void Append_handles_multiple_attachments_in_order()
    {
        var report = OnePagePdf();
        var a = OnePagePdf();
        var b = OnePagePdf();
        var aPath = WriteTempPdf(a);
        var bPath = WriteTempPdf(b);
        try
        {
            var merged = PdfAttachmentMerger.Append(report, new[] { aPath, bPath });

            Assert.Equal(
                PdfAssert.CountPages(report) + PdfAssert.CountPages(a) + PdfAssert.CountPages(b),
                PdfAssert.CountPages(merged));
        }
        finally
        {
            File.Delete(aPath);
            File.Delete(bPath);
        }
    }

    [Fact]
    public void Append_throws_a_clear_error_for_a_missing_attachment_path()
    {
        var report = OnePagePdf();
        var missingPath = Path.Combine(Path.GetTempPath(), $"lagebuch-pdf-merger-test-missing-{Guid.NewGuid():N}.pdf");

        Assert.Throws<FileNotFoundException>(() => PdfAttachmentMerger.Append(report, new[] { missingPath }));
    }
}
