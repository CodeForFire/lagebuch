using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Sync.Hosting.Tests;

internal sealed class InMemoryStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _byPath = new();
    public void Save(string path, Incident incident) => _byPath[path] = incident;
    public Incident Load(string path) => _byPath[path];
    public IncidentState? TryReadState(string path) => _byPath.TryGetValue(path, out var i) ? i.State : null;
    private readonly Dictionary<string, byte[]> _files = new();
    public void SaveFileBytes(string path, string storageFileName, byte[] bytes) => _files[$"{path}/{storageFileName}"] = bytes;
    public byte[]? TryReadFileBytes(string path, string storageFileName) => _files.TryGetValue($"{path}/{storageFileName}", out var b) ? b : null;
}

internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
}

// Minimal service doubles for constructing ViewModels (IncidentWorkspaceViewModel/HomeViewModel).
internal sealed class NoTicker : ITicker
{
    public IDisposable Subscribe(Action onTick) => new Sub();
    private sealed class Sub : IDisposable { public void Dispose() { } }
}

internal sealed class NoAlarm : IAlarmService
{
    [SuppressMessage("Performance", "CA1822", Justification = "Implements IAlarmService; interface members cannot be static.")]
    public void Start() { }
    [SuppressMessage("Performance", "CA1822", Justification = "Implements IAlarmService; interface members cannot be static.")]
    public void Stop() { }
    public void Play(AlarmSound sound) { }
}

internal sealed class NoDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string suggestedFileName, string? initialFolder = null) => Task.FromResult<string?>(null);
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task<string?> PickAttachmentAsync() => Task.FromResult<string?>(null);
    public Task OpenFileAsync(string path) => Task.CompletedTask;
    public Task OpenUrlAsync(string url) => Task.CompletedTask;
    public Task ShareFileAsync(string path, string mimeType) => Task.CompletedTask;
}

internal sealed class EmptyMasterData : IMasterDataProvider
{
    public MasterDataSet Get() => MasterDataSet.Empty;
    public void Save(MasterDataSet set) { }
}

internal sealed class NoRecentFiles : IRecentFilesStore
{
    public IReadOnlyList<string> GetRecent() => Array.Empty<string>();
    public void Add(string path) { }
}

internal static class TestHost
{
    /// <summary>The share PIN the test host runs with; clients pass this to ConnectAsync to be let in.</summary>
    public const string DefaultPin = "1234";

    public static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<(IncidentHost host, int port)> StartAsync(
        LocalIncidentSession session, IClock clock, string version = "1.0.0", IUiDispatcher? ui = null,
        string pin = DefaultPin)
    {
        var host = new IncidentHost(session, clock, version, ui ?? new ImmediateUiDispatcher(), pin);
        var port = FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);
        return (host, port);
    }
}

/// <summary>
/// An <see cref="IUiDispatcher"/> that runs every callback on one dedicated background thread, standing
/// in for the app's single UI thread. Lets a test assert that a host broadcast is marshalled onto that
/// thread (as the real Avalonia dispatcher demands) rather than mutating bound state on SignalR's
/// receive loop or a Kestrel request thread.
/// </summary>
internal sealed class SingleThreadUiDispatcher : IUiDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public SingleThreadUiDispatcher()
    {
        _thread = new Thread(() =>
        {
            foreach (var work in _queue.GetConsumingEnumerable())
                work();
        }) { IsBackground = true, Name = "test-ui-thread" };
        _thread.Start();
    }

    /// <summary>The managed id of the stand-in UI thread, to compare against where work actually ran.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    public void Post(Action action) => _queue.Add(action);

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        // CA1031: exceptions are captured here, not swallowed — delivered to the await-er via the TCS.
#pragma warning disable CA1031
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
#pragma warning restore CA1031
        return tcs.Task;
    }

    public void Dispose() => _queue.CompleteAdding();
}
