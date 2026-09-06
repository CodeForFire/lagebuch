using LageBuch.AppLogic.Services;
using LageBuch.Documents;
using LageBuch.Domain;

namespace LageBuch.App.Services;

/// <summary>
/// Desktop implementation of <see cref="IIncidentPdfExporter"/>: the only head that renders via
/// QuestPDF (<see cref="IncidentPdf"/>), since QuestPDF doesn't support Android
/// (QuestPDF/QuestPDF#1432). Lives in the desktop head so QuestPDF/LageBuch.Documents stay out of
/// the cross-platform AppLogic/Android build.
/// </summary>
internal sealed class QuestPdfIncidentExporter : IIncidentPdfExporter
{
    public bool CanExport => true;

    public byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths) =>
        IncidentPdf.Generate(incident, fileBytes, pdfAttachmentPaths);
}
