using LageBuch.AppLogic.Services;
using LageBuch.Domain;

namespace LageBuch.Acceptance.Tests;

// Lets EXPORTIEREN run: the default exporter is the Android one, which hides the button.
internal sealed class ExportablePdf : IIncidentPdfExporter
{
    public bool CanExport => true;

    public byte[] Generate(Incident incident, DateTimeOffset asOf, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All) =>
        Array.Empty<byte>();
}
