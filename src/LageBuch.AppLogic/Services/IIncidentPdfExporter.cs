using LageBuch.Domain;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Platform hook for rendering an incident to a PDF (mirrors <see cref="IIncidentHostController"/>).
/// The desktop head implements it with QuestPDF-backed <c>LageBuch.Documents</c>; Android supplies
/// <see cref="NoopIncidentPdfExporter"/> instead of referencing that project, since QuestPDF doesn't
/// support Android (see issue #184 / QuestPDF/QuestPDF#1432) and pulling it in there only produced
/// build warnings for a feature that can't run. Kept in AppLogic so the cross-platform session/view
/// model depend only on this interface, never on QuestPDF.
/// </summary>
public interface IIncidentPdfExporter
{
    /// <summary>Whether this platform can export at all (false hides the "Export PDF" button).</summary>
    bool CanExport { get; }

    byte[] Export(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths);
}

/// <summary>No-op exporter for heads that can't export; the button stays hidden (<see cref="CanExport"/> is false).</summary>
public sealed class NoopIncidentPdfExporter : IIncidentPdfExporter
{
    public bool CanExport => false;

    public byte[] Export(Incident incident, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths) =>
        throw new NotSupportedException("PDF export is not supported on this platform.");
}
