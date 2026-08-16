using System.Net;
using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain.Time;
using Feuerwehr.Sync;
using Feuerwehr.Sync.Hosting;

namespace Feuerwehr.App.Services;

/// <summary>
/// Desktop implementation of <see cref="IIncidentHostController"/>: drives the embedded
/// <see cref="IncidentHost"/>, binding it to every interface (<see cref="IPAddress.Any"/>) so it is
/// reachable over loopback, the LAN, and a tailnet at once. Lives in the desktop head so ASP.NET
/// Core stays out of the cross-platform AppLogic/Android build.
/// </summary>
public sealed class IncidentHostController : IIncidentHostController
{
    private readonly IClock _clock;
    private readonly string _appVersion;
    private readonly IUiDispatcher _ui;
    private IncidentHost? _host;

    public IncidentHostController(IClock clock, string appVersion, IUiDispatcher ui)
    {
        _clock = clock;
        _appVersion = appVersion;
        _ui = ui;
    }

    public bool CanHost => true;
    public bool IsHosting => _host?.IsRunning ?? false;
    public string? ShareHint { get; private set; }

    public async Task StartAsync(LocalIncidentSession session)
    {
        if (_host is not null)
            return;
        var host = new IncidentHost(session, _clock, _appVersion, _ui);
        await host.StartAsync(IPAddress.Any);
        _host = host;
        // Bound on every interface; show the nicest address to dial plus the same-machine shortcut.
        ShareHint = $"Erreichbar unter {LocalNetwork.DisplayAddress()}:{SyncProtocol.Port} · "
            + $"auf diesem Gerät: localhost:{SyncProtocol.Port}";
    }

    public async Task StopAsync()
    {
        if (_host is null)
            return;
        await _host.DisposeAsync();
        _host = null;
        ShareHint = null;
    }
}
