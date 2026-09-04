using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;

namespace LageBuch.AppLogic.Services;

public interface IIncidentStore
{
    /// <summary>
    /// Queues <paramref name="incident"/> to be written to <paramref name="path"/> and returns once
    /// the write is queued — not once it is durable. Writes queued for the same store are applied in
    /// the order <see cref="Save"/> was called. Call <see cref="FlushAsync"/> to wait for every queued
    /// write to complete, e.g. before the app exits.
    /// </summary>
    void Save(string path, Incident incident);

    /// <summary>Waits for every write queued so far via <see cref="Save"/> to complete.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);

    Incident Load(string path);

    /// <summary>
    /// Cheaply peeks at an incident file's lifecycle state without loading or migrating it, for the
    /// Home overview's closed marker. Returns null when the file is missing, unreadable, or otherwise
    /// cannot be inspected — the overview then shows no marker rather than failing.
    /// </summary>
    IncidentState? TryReadState(string path);

    /// <summary>
    /// Streams an attached file's bytes straight to storage without materializing the whole payload
    /// in memory first (issue #167 P1 #2). Genuinely async (real disk I/O, not queued like
    /// <see cref="Save"/>) — see issue #167 P1 #1: unlike <see cref="Save"/>, a file write has no
    /// ordering dependency on other writes, so real async I/O is enough to free the caller without a
    /// second queuing mechanism. See <c>LageBuch.Persistence.IIncidentFileStore.SaveStreamAsync</c>.
    /// </summary>
    Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default);

    /// <summary>The real path on disk for an attached file, for APIs that require a file path
    /// rather than bytes (QuestPDF's <c>DocumentOperation</c> and <c>Image</c> — see issue #167
    /// P1 #3). Does not guarantee the file exists — callers check <see cref="File.Exists"/> or open
    /// it for read and handle absence themselves.</summary>
    string ResolveFileDiskPath(string path, string storageFileName);

    /// <summary>
    /// Raised on the background writer thread when a queued <see cref="Save"/> throws (the queue
    /// keeps serving later writes regardless). Fires off the UI thread — marshal it yourself (e.g.
    /// via <c>IUiDispatcher</c>) before touching UI-bound state from a handler.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1003",
        Justification = "A plain Exception payload doesn't warrant a bespoke EventArgs type; matches this codebase's other events (e.g. IIncidentSession.Changed), which favor plain delegates over the sender/EventArgs pattern.")]
    event Action<Exception>? SaveFailed;
}
