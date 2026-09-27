using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain;
using LageBuch.Domain.Files;

namespace LageBuch.AppLogic.Tests;

public class SessionPdfExportTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 14, 5, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Host, FakeStore Store) HostSession()
    {
        var store = new FakeStore();
        var host = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/y.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (host, store);
    }

    [Fact]
    public async Task Client_export_merges_a_pdf_attachment_via_a_temp_file_and_removes_it_afterwards()
    {
        var (host, _) = HostSession();
        var client = new SnapshotRoundTrippingSession(host);
        var exporter = new RecordingPdfExporter();

        var withoutAttachment = await SessionPdfExport.ExportAsync(client, exporter, new FixedClock(T0));
        await host.AddFileAsync("bericht.pdf", "application/pdf", withoutAttachment.Pdf);

        var withAttachment = await SessionPdfExport.ExportAsync(client, exporter, new FixedClock(T0));

        Assert.Equal(0, withAttachment.MissingAttachments);
        Assert.True(
            withAttachment.Pdf.Length > withoutAttachment.Pdf.Length,
            $"Expected the attached PDF's pages to be merged (without={withoutAttachment.Pdf.Length}, with={withAttachment.Pdf.Length}).");

        // The copy existed while the report was rendered, lived in the app's own temp area under a
        // name of our choosing — not the peer's "bericht.pdf" — and is gone once the export returns.
        var path = Assert.Single(exporter.SeenPdfAttachmentPaths);
        Assert.True(exporter.PathsExistedDuringGenerate);
        Assert.StartsWith(AttachmentTempPaths.Root, path, StringComparison.Ordinal);
        Assert.DoesNotContain("bericht", Path.GetFileName(path), StringComparison.Ordinal);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task Client_export_skips_an_attachment_the_host_cannot_deliver_and_counts_it()
    {
        var (host, store) = HostSession();
        await host.AddFileAsync("foto.png", "image/png", [0x89, 0x50, 0x4E, 0x47]);
        var file = Assert.Single(host.Incident.Files);

        // Neither cached nor deliverable: GetFileBytesAsync comes back null, as it does on a client
        // whose host has gone away before the attachment was ever opened.
        await store.DeleteFileBytesAsync("/y.fwincident", IncidentFile.StorageFileName(file.Id, file.FileName));
        var client = new SnapshotRoundTrippingSession(host);

        var result = await SessionPdfExport.ExportAsync(client, new TestPdfExporter(), new FixedClock(T0));

        Assert.Equal(1, result.MissingAttachments);
        Assert.Equal(0x25, result.Pdf[0]); // %PDF -- the report itself still renders
    }

    // Records what the exporter was handed, and whether the PDF attachments it was pointed at were
    // really on disk at that moment, then renders for real.
    private sealed class RecordingPdfExporter : IIncidentPdfExporter
    {
        public List<string> SeenPdfAttachmentPaths { get; } = new();

        public bool PathsExistedDuringGenerate { get; private set; } = true;

        public bool CanExport => true;

        public byte[] Generate(Incident incident, DateTimeOffset asOf, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All)
        {
            SeenPdfAttachmentPaths.Clear();
            SeenPdfAttachmentPaths.AddRange(pdfAttachmentPaths.Values);
            PathsExistedDuringGenerate &= pdfAttachmentPaths.Values.All(File.Exists);
            return IncidentPdf.Generate(incident, asOf, fileBytes, pdfAttachmentPaths, sections);
        }
    }
}
