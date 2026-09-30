using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using LageBuch.Domain;
using LageBuch.Persistence;
using LageBuch.Sync;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Saves run on a single dedicated background thread (mirroring <c>SerialAudioQueue</c>) so
/// <see cref="Save"/> never blocks its caller on SQLite I/O — see issue #167 P0 #1. <see cref="Save"/>
/// captures a cheap, independent snapshot of the aggregate on the calling thread, then hands the
/// actual write to the worker; writes land in the order <see cref="Save"/> was called, since one
/// thread drains one FIFO queue. A queued write whose path has since been saved again is dropped:
/// every save carries the whole aggregate, so only the newest snapshot of a path is worth writing
/// once the writer falls behind a burst of edits (issue #290).
/// </summary>
[SuppressMessage(
    "Design",
    "CA1001",
    Justification = "App-lifetime singleton: its worker thread drains until process shutdown, so the owning BlockingCollection is intentionally never disposed.")]
public sealed class IncidentStore : IIncidentStore
{
    private readonly IncidentFileStore _fileStore = new IncidentFileStore();
    private readonly Action<string, Incident> _write;
    private readonly BlockingCollection<Action> _writeQueue = new();
    private readonly ConcurrentDictionary<string, IncidentSnapshot> _latest = new(StringComparer.Ordinal);

    public IncidentStore()
        : this(IncidentRepository.Save)
    {
    }

    /// <summary>Lets tests observe/control the underlying write independently of the queuing behavior.</summary>
    internal IncidentStore(Action<string, Incident> write)
    {
        _write = write;
        var worker = new Thread(RunWriter) { IsBackground = true, Name = nameof(IncidentStore) + "Writer" };
        worker.Start();
    }

    public event Action<Exception>? SaveFailed;

    public event Action? SaveSucceeded;

    [SuppressMessage(
        "Design",
        "CA1031",
        Justification = "A failed write must reach SaveFailed, not crash the background writer thread or its caller.")]
    public void Save(string path, Incident incident)
    {
        // Publish before enqueueing: the work item below, or an earlier one for the same path that
        // runs first, is then guaranteed to find this snapshot or a newer one. A work item that
        // finds nothing was beaten to it and has nothing left to write. That also keeps FlushAsync
        // honest -- its marker queues behind the work item of every snapshot saved before it.
        _latest[path] = SnapshotMapper.ToSnapshot(incident);
        _writeQueue.Add(() =>
        {
            if (!_latest.TryRemove(path, out var snapshot))
            {
                return;
            }

            try
            {
                _write(path, SnapshotMapper.FromSnapshot(snapshot));
                SaveSucceeded?.Invoke();
            }
            catch (Exception ex)
            {
                SaveFailed?.Invoke(ex);
            }
        });
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _writeQueue.Add(() => tcs.TrySetResult(), CancellationToken.None);
        return tcs.Task;
    }

    public Incident Load(string path) => IncidentRepository.Load(path);

    public IncidentState? TryReadState(string path) => IncidentRepository.TryReadState(path);

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        _fileStore.SaveBytesAsync(path, storageFileName, bytes, cancellationToken);

    public Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        _fileStore.SaveStreamAsync(path, storageFileName, source, cancellationToken);

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        _fileStore.TryReadBytesAsync(path, storageFileName, cancellationToken);

    public string ResolveFileDiskPath(string path, string storageFileName) =>
        _fileStore.ResolveDiskPath(path, storageFileName);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        _fileStore.DeleteBytesAsync(path, storageFileName, cancellationToken);

    private void RunWriter()
    {
        foreach (var work in _writeQueue.GetConsumingEnumerable())
        {
            work();
        }
    }
}
