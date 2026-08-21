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

    [Fact]
    public void Append_with_no_attachments_returns_the_report_unchanged()
    {
        var report = OnePagePdf();

        var result = PdfAttachmentMerger.Append(report, Array.Empty<byte[]>());

        Assert.Same(report, result);
    }

    [Fact]
    public void Append_adds_the_attachments_pages_after_the_reports_own()
    {
        var report = OnePagePdf();
        var reportPages = PdfAssert.CountPages(report);
        var attachment = OnePagePdf(); // any valid PDF stands in for "an attached PDF"

        var merged = PdfAttachmentMerger.Append(report, new[] { attachment });

        PdfAssert.IsPdf(merged);
        Assert.Equal(reportPages + PdfAssert.CountPages(attachment), PdfAssert.CountPages(merged));
    }

    [Fact]
    public void Append_handles_multiple_attachments_in_order()
    {
        var report = OnePagePdf();
        var a = OnePagePdf();
        var b = OnePagePdf();

        var merged = PdfAttachmentMerger.Append(report, new[] { a, b });

        Assert.Equal(
            PdfAssert.CountPages(report) + PdfAssert.CountPages(a) + PdfAssert.CountPages(b),
            PdfAssert.CountPages(merged));
    }
}
