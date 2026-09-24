using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// PDF export for a joined client (#465), which has no .fwincident of its own. The report is built
/// from the synced <see cref="IIncidentSession.Incident"/>; every attachment's bytes come through
/// <see cref="IIncidentSession.GetFileBytesAsync"/>, which reads the client's attachment cache and
/// only falls back to pulling from the host. The host keeps using
/// <see cref="LocalIncidentSession.ExportPdfAsync"/>, which merges PDF attachments straight from
/// its own disk.
/// </summary>
public static class SessionPdfExport
{
    /// <summary>
    /// Renders the session's incident. <c>MissingAttachments</c> counts the attachments left out
    /// because their bytes could not be had — neither cached nor deliverable by the host right now.
    /// </summary>
    public static async Task<(byte[] Pdf, int MissingAttachments)> ExportAsync(
        IIncidentSession session,
        IIncidentPdfExporter exporter,
        IClock clock,
        IncidentPdfSections sections = IncidentPdfSections.All,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(clock);

        var incident = session.Incident;
        var fileBytes = new Dictionary<Guid, byte[]>();
        var pdfAttachmentPaths = new Dictionary<Guid, string>();
        var missing = 0;

        // The PDF merger reads attachments from disk, so a PDF attachment's bytes are written to a
        // private directory for the duration of the export. Named by id, never by the file name a
        // peer chose — nothing that crossed the trust boundary becomes part of a path.
        string? tempDirectory = null;
        try
        {
            foreach (var file in incident.Files)
            {
                var bytes = await session.GetFileBytesAsync(file.Id, cancellationToken);
                if (bytes is null)
                {
                    missing++;
                    continue;
                }

                if (file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
                {
                    tempDirectory ??= AttachmentTempPaths.CreateOpenDirectory();
                    var path = Path.Join(tempDirectory, file.Id.ToString("N") + ".pdf");
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                    pdfAttachmentPaths[file.Id] = path;
                    continue;
                }

                fileBytes[file.Id] = bytes;
            }

            // One timestamp for the whole document, as on the host.
            var pdf = exporter.Generate(incident, clock.Now, fileBytes, pdfAttachmentPaths, sections);
            return (pdf, missing);
        }
        finally
        {
            if (tempDirectory is not null)
            {
                DeleteQuietly(tempDirectory);
            }
        }
    }

    // Best-effort: the copies sit in the app's own temp area, and a leftover file must not turn a
    // PDF that was already rendered into a failed export.
    private static void DeleteQuietly(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the OS's temp cleanup; see the comment above.
        }
    }
}
