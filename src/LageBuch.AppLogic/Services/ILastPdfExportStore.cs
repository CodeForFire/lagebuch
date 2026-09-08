namespace LageBuch.AppLogic.Services;

/// <summary>
/// Remembers the path and time of the most recent PDF export (#262), so the workspace can show a
/// persistent "zuletzt exportiert" status line even after reopening the incident. Desktop-only in
/// practice, same reasoning as <see cref="ILastSaveFolderStore"/> — Android has no PDF export at
/// all (NoopIncidentPdfExporter hides the button).
/// </summary>
public interface ILastPdfExportStore
{
    LastPdfExport? GetLastExport();

    void SetLastExport(string path, DateTimeOffset exportedAt);
}

/// <summary>The path and time of the most recent PDF export, persisted by <see cref="ILastPdfExportStore"/>.</summary>
public readonly record struct LastPdfExport(string Path, DateTimeOffset ExportedAt);
