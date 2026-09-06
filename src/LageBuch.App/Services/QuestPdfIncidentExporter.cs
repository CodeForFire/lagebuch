using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain;

namespace LageBuch.App.Services;

/// <summary>
/// Desktop implementation of <see cref="IIncidentPdfExporter"/>: renders via QuestPDF-backed
/// <see cref="IncidentPdf"/>. Lives in the desktop head so QuestPDF stays out of AppLogic's (and
/// therefore Android's) dependency graph — see issue #184.
/// </summary>
public sealed class QuestPdfIncidentExporter : IIncidentPdfExporter
{
    public bool CanExport => true;

    public byte[] Export(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths) =>
        IncidentPdf.Generate(incident, fileBytes, pdfAttachmentPaths);
}
