using Feuerwehr.AppLogic;
using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain.Time;
using Feuerwehr.Sync;
using Feuerwehr.Sync.Hosting;

namespace Feuerwehr.App.Services;

/// <summary>
/// Desktop implementation of <see cref="IIncidentHostController"/>: drives the embedded
/// <see cref="IncidentHost"/>, binding it to the device's Tailscale address. Lives in the desktop
/// head so ASP.NET Core stays out of the cross-platform AppLogic/Android build.
/// </summary>
public sealed class IncidentHostController : IIncidentHostController
{
    private readonly IClock _clock;
    private readonly string _appVersion;
    private IncidentHost? _host;

    public IncidentHostController(IClock clock, string appVersion)
    {
        _clock = clock;
        _appVersion = appVersion;
    }

    public bool CanHost => true;
    public bool IsTailscaleConnected => TailscaleNetwork.IsConnected();
    public bool IsHosting => _host?.IsRunning ?? false;
    public string? ShareHint { get; private set; }

    public async Task StartAsync(LocalIncidentSession session)
    {
        if (_host is not null)
            return;
        var address = TailscaleNetwork.LocalAddress()
            ?? throw new InvalidOperationException("Tailscale nicht verbunden.");
        var host = new IncidentHost(session, _clock, _appVersion);
        await host.StartAsync(address);
        _host = host;
        ShareHint = $"Erreichbar unter {address}:{SyncProtocol.Port}";
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
