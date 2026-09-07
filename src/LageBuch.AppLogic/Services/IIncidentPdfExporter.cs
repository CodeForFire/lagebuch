using LageBuch.Domain;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Platform hook for rendering an incident to a PDF. The renderer (QuestPDF) doesn't support
/// Android (QuestPDF/QuestPDF#1432), so only heads that can render supply a real implementation;
/// Android supplies <see cref="NoopIncidentPdfExporter"/> and hides the Export PDF button. Kept in
/// AppLogic so the cross-platform session/ViewModel never reference QuestPDF or LageBuch.Documents,
/// which keeps QuestPDF (and its Android build warnings) entirely out of that head's dependency graph.
/// </summary>
public interface IIncidentPdfExporter
{
    /// <summary>Whether this platform can export at all (false hides the button).</summary>
    bool CanExport { get; }

    byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All);
}

/// <summary>No-op exporter for heads that cannot render PDFs; the button stays hidden (<see cref="CanExport"/> is false).</summary>
public sealed class NoopIncidentPdfExporter : IIncidentPdfExporter
{
    public bool CanExport => false;

    public byte[] Generate(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All) =>
        throw new NotSupportedException("PDF export is not available on this platform.");
}
