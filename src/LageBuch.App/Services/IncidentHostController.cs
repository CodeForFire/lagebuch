using System.Globalization;
using System.Net;
using LageBuch.AppLogic;
using LageBuch.AppLogic.Services;
using LageBuch.Domain.Time;
using LageBuch.Persistence.MasterData;
using LageBuch.Sync;
using LageBuch.Sync.Hosting;

namespace LageBuch.App.Services;

/// <summary>
/// Desktop implementation of <see cref="IIncidentHostController"/>: drives the embedded
/// <see cref="IncidentHost"/>, binding it to every interface (<see cref="IPAddress.Any"/>, which
/// <see cref="IncidentHost.StartAsync"/> turns into a dual-stack IPv4+IPv6 bind) so it is reachable
/// over loopback, the LAN (v4 or v6), and a tailnet at once. Lives in the desktop head so ASP.NET
/// Core stays out of the cross-platform AppLogic/Android build.
/// </summary>
internal sealed class IncidentHostController : IIncidentHostController
{
    private readonly IClock _clock;
    private readonly string _appVersion;
    private readonly IUiDispatcher _ui;
    private readonly string _identityKeyPath;
    private IncidentHost? _host;

    public IncidentHostController(IClock clock, string appVersion, IUiDispatcher ui, string identityKeyPath)
    {
        _clock = clock;
        _appVersion = appVersion;
        _ui = ui;
        _identityKeyPath = identityKeyPath;
    }

    public bool CanHost => true;

    public bool IsHosting => _host?.IsRunning ?? false;

    public string? ShareHint { get; private set; }

    public string? SharePin { get; private set; }

    public bool JoinsClosed => _host?.JoinsClosed ?? false;

    public event EventHandler? JoinsClosedChanged;

    public string? ShareKennung { get; private set; }

    public async Task StartAsync(LocalIncidentSession session, MasterDataSet masterData, CancellationToken cancellationToken = default)
    {
        if (_host is not null)
        {
            return;
        }

        var pin = NewPin();

        // The persistent key is what lets joined devices recognise this host on the next share.
        var host = new IncidentHost(session, _clock, _appVersion, _ui, pin, masterData, identityKeyPath: _identityKeyPath);
        await host.StartAsync(IPAddress.Any, cancellationToken: cancellationToken);
        host.JoinsClosedChanged += OnJoinsClosedChanged;
        _host = host;
        SharePin = pin;
        ShareKennung = host.Kennung;

        // Bound on every interface; show the nicest address to dial plus the same-machine shortcut.
        // One per line: the flyout that shows it supplies the "Erreichbar unter" heading.
        ShareHint = $"Im Netzwerk: https://{LocalNetwork.DisplayAddress()}:{SyncProtocol.Port}\n"
            + $"Auf diesem Gerät: https://localhost:{SyncProtocol.Port}";
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host is null)
        {
            return;
        }

        _host.JoinsClosedChanged -= OnJoinsClosedChanged;
        await _host.DisposeAsync();
        _host = null;
        ShareHint = null;
        SharePin = null;
        ShareKennung = null;
    }

    public void RenewPin()
    {
        if (_host is null)
        {
            return;
        }

        // SharePin first: ReplacePin raises JoinsClosedChanged, and whoever handles it reads the
        // new PIN from here.
        var pin = NewPin();
        SharePin = pin;
        _host.ReplacePin(pin);
    }

    // A fresh 4-digit PIN per share session, and per renewal: the host reads it out, joiners type
    // it (§ #64). Cryptographic RNG so the PIN isn't predictable from a seeded/observed sequence.
    // Four digits hold because the host spends a budget of wrong PINs across every address and
    // then closes joins until a person renews (#288, JoinGate) — not because the space is large.
    private static string NewPin() =>
        System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 10_000).ToString("D4", CultureInfo.InvariantCulture);

    private void OnJoinsClosedChanged(object? sender, EventArgs e) => JoinsClosedChanged?.Invoke(this, e);
}
