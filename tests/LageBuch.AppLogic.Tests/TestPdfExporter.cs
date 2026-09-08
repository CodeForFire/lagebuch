using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Tests;

// Real QuestPDF-backed double -- tests that assert on actual PDF bytes (rather than just wiring)
// need this instead of NoopIncidentPdfExporter, which throws.
internal sealed class TestPdfExporter : IIncidentPdfExporter
{
    public bool CanExport => true;

    public byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All) =>
        IncidentPdf.Generate(incident, fileBytes, pdfAttachmentPaths, sections);
}
