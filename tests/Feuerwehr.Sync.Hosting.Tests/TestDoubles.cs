using System.Net;
using System.Net.Sockets;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Persistence.MasterData;

namespace Feuerwehr.Sync.Hosting.Tests;

internal sealed class InMemoryStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _byPath = new();
    public void Save(string path, Incident incident) => _byPath[path] = incident;
    public Incident Load(string path) => _byPath[path];
    public IncidentState? TryReadState(string path) => _byPath.TryGetValue(path, out var i) ? i.State : null;
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
    public void Start() { }
    public void Stop() { }
}

internal sealed class NoDialogs : IFileDialogService
{
    public Task<string?> PickSaveAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task<string?> PickOpenAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportPdfAsync(string suggestedFileName) => Task.FromResult<string?>(null);
    public Task<string?> PickImportJsonAsync() => Task.FromResult<string?>(null);
    public Task<string?> PickExportJsonAsync(string suggestedFileName) => Task.FromResult<string?>(null);
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
    public static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static async Task<(IncidentHost host, int port)> StartAsync(
        LocalIncidentSession session, IClock clock, string version = "1.0.0")
    {
        var host = new IncidentHost(session, clock, version);
        var port = FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);
        return (host, port);
    }
}
