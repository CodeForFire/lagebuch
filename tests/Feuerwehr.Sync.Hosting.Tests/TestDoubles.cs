using System.Net;
using System.Net.Sockets;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;

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
