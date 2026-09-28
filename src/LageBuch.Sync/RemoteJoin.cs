using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using LageBuch.Domain;
using Microsoft.AspNetCore.SignalR.Client;

namespace LageBuch.Sync;

/// <summary>
/// The first half of joining another device's hosted incident (#459): the host has been reached,
/// the PIN and wire contract accepted, and its Stammdaten and incident read — but no push channel
/// is open and nobody has said who documents on this device yet. That gap is what lets the join
/// dialog offer the host's own personnel and call signs as suggestions, and name the incident being
/// joined, before the Lagebuchführer is asked for.
/// <para>
/// <see cref="ConnectAsync"/> completes the join and hands the connection over to the returned
/// <see cref="RemoteIncidentSession"/>; <see cref="DisposeAsync"/> abandons it. Either one, once.
/// </para>
/// </summary>
public sealed class RemoteJoin : IAsyncDisposable
{
    private readonly HttpClient _http;
    private readonly HttpClientHandler _handler;
    private readonly Uri _baseUri;
    private readonly string? _pin;
    private readonly IncidentSnapshot _snapshot;

    // 0 while this join still owns the connection; 1 once ConnectAsync handed it to a session or
    // DisposeAsync released it. Whichever comes first wins, so the connection is disposed exactly once.
    private int _spent;

    private RemoteJoin(HttpClient http, HttpClientHandler handler, Uri baseUri, string? pin, IncidentSnapshot snapshot, string hostMasterDataJson)
    {
        _http = http;
        _handler = handler;
        _baseUri = baseUri;
        _pin = pin;
        _snapshot = snapshot;
        Incident = SnapshotMapper.FromSnapshot(snapshot);
        HostMasterDataJson = hostMasterDataJson;
    }

    /// <summary>
    /// The host's Stammdaten, verbatim, in the <c>MasterDataJson</c> interchange format (#183). Left
    /// unparsed for the same reason as <see cref="RemoteIncidentSession.HostMasterDataJson"/>.
    /// </summary>
    public string HostMasterDataJson { get; }

    /// <summary>
    /// The incident as the host had it when the join was opened. Only a preview: the session that
    /// <see cref="ConnectAsync"/> returns catches up on anything that changed in between.
    /// </summary>
    public Incident Incident { get; }

    /// <summary>
    /// Reaches the host: version handshake, then Stammdaten and snapshot. Throws exactly what
    /// <see cref="RemoteIncidentSession.ConnectAsync"/> documents for these steps —
    /// <see cref="PinRejectedException"/>, <see cref="VersionMismatchException"/>,
    /// <see cref="CertificateChangedException"/>, or <see cref="HttpRequestException"/> for a host that
    /// isn't sharing or can't be reached. The parameters are the same as there.
    /// </summary>
    public static async Task<RemoteJoin> OpenAsync(
        string host,
        string localVersion,
        ITrustStore trustStore,
        string? pin = null,
        int port = SyncProtocol.Port,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trustStore);

        var baseUri = new Uri($"https://{host}:{port}");

        // A single handler backs both the HttpClient and the SignalR hub connection, so they agree on
        // TLS validation: pin the presented cert via Trust-on-First-Use. A certificate that differs
        // from the previously-trusted one throws CertificateChangedException from inside the callback;
        // the connect await surfaces it (§ P0 #2).
        var handler = new HttpClientHandler { CheckCertificateRevocationList = true };
        handler.ServerCertificateCustomValidationCallback = (_, cert, _, _) =>
        {
            if (cert is null)
            {
                return false;
            }

            // Pin the host's key, not its certificate: the host issues a new certificate per share
            // but keeps its key, so a restarted share or a new incident is still the same host.
            var presented = HostKennung.Pin(cert);
            var known = trustStore.GetThumbprint(host);
            if (known is null)
            {
                trustStore.SaveThumbprint(host, presented);
                return true;
            }

            if (!known.StartsWith(HostKennung.PinPrefix, StringComparison.Ordinal))
            {
                // A whole-certificate hash from before the persistent key. Upgraded only when this
                // very certificate still matches it; anything else is a changed host like any other,
                // never a silent first contact.
                if (!string.Equals(known, Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256)), StringComparison.OrdinalIgnoreCase))
                {
                    throw new CertificateChangedException(host, presented, HostKennung.FromPin(presented), HostKennung.ShowsKennung(cert));
                }

                trustStore.SaveThumbprint(host, presented);
                return true;
            }

            if (string.Equals(known, presented, StringComparison.Ordinal))
            {
                return true;
            }

            // Same Kennung, different key: someone ground a key to pass the comparison with the
            // host's screen. Refused outright, with nothing offered to trust.
            if (string.Equals(HostKennung.FromPin(known), HostKennung.FromPin(presented), StringComparison.Ordinal))
            {
                throw CertificateChangedException.Forged(host);
            }

            throw new CertificateChangedException(host, presented, HostKennung.FromPin(presented), HostKennung.ShowsKennung(cert));
        };

        // disposeHandler: false — the handler's owner disposes it explicitly (this join until it is
        // handed over, the session after that — which disposes it after the hub, since the hub's
        // long-lived transport also uses it) rather than relying on HttpClient's default cascade, so
        // ownership is one clear line instead of implicit via a constructor flag.
        // MaxResponseContentBufferSize: every response here is read into memory, and each one comes
        // from a sync peer, so none may be larger than SyncProtocol.MaxResponseBytes.
        var http = new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = baseUri,
            MaxResponseContentBufferSize = SyncProtocol.MaxResponseBytes,
        };
        if (!string.IsNullOrEmpty(pin))
        {
            http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        }

        // Announced on every request, not just the handshake, so the host can refuse a contract it no
        // longer serves at whichever endpoint the client reaches for. Unconditional — unlike the PIN
        // there is nothing optional about which contract this build speaks. The hub needs the same
        // header set on its own options bag: sharing the handler does not share these.
        http.DefaultRequestHeaders.Add(
            SyncProtocol.ProtocolHeader,
            SyncProtocol.ProtocolVersion.ToString(CultureInfo.InvariantCulture));

        try
        {
            // The PIN gates every endpoint, so the first request already reflects it: a 401 means the
            // PIN is wrong/missing — reported as such before the version compare (auth precedes content).
            // A cert that differs from the trusted one makes the TLS handshake fail: .NET wraps the
            // CertificateChangedException the callback threw in an HttpRequestException, so unwrap and
            // rethrow it so the cert change surfaces as its typed exception, not an opaque HTTP error.
            HttpResponseMessage versionResponse;
            try
            {
                versionResponse = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute), ct);
            }
            catch (HttpRequestException ex) when (FindInner<CertificateChangedException>(ex) is { } certChanged)
            {
                throw certChanged;
            }

            if (versionResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw new PinRejectedException();
            }

            // Only a host older than #288 throttles wrong PINs; this build's host answers every one
            // with a 401 and counts it. The fleet is mixed, so the wait still has to be named.
            if (versionResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = versionResponse.Headers.RetryAfter?.Delta?.TotalSeconds ?? 60;
                throw new PinRejectedException($"Zu viele Fehlversuche. Bitte {retryAfter:F0}s warten.");
            }

            // A host that refuses this build's contract outright. It cannot happen while /version is
            // exempt from the host's own protocol gate — which it is, precisely so the payload below
            // can name the versions — but a future host may stop exempting it, and a bare
            // HttpRequestException would then reach the user as "Teilt dieses Gerät gerade einen
            // Einsatz?", which sends them looking in the wrong place entirely.
            if (versionResponse.StatusCode == HttpStatusCode.UpgradeRequired)
            {
                throw VersionMismatchException.ThisDeviceIsTooOld(localVersion, "unbekannt");
            }

            versionResponse.EnsureSuccessStatusCode();

            // Compatibility is decided on the wire contract, never on the app version (§7): the two
            // ends of a volunteer-run fleet are routinely on different releases while speaking an
            // identical protocol, and an Android update additionally waits on Play review. A host
            // that predates this handshake sends neither number, so 0 means LegacyProtocolVersion.
            var hostInfo = SyncJson.Deserialize<VersionInfo>(await versionResponse.Content.ReadAsStringAsync(ct));
            var hostProtocol = hostInfo.Protocol == 0 ? SyncProtocol.LegacyProtocolVersion : hostInfo.Protocol;
            var hostMinProtocol = hostInfo.MinProtocol == 0 ? SyncProtocol.LegacyProtocolVersion : hostInfo.MinProtocol;

            // The client is the end that sees both ranges, so it is the end that decides; the host's
            // own gate is a one-sided backstop against a peer below its floor. The app version goes
            // into the message so a human is told which of the two devices to update.
            if (hostMinProtocol > SyncProtocol.ProtocolVersion)
            {
                throw VersionMismatchException.ThisDeviceIsTooOld(localVersion, hostInfo.Version);
            }

            if (hostProtocol < SyncProtocol.MinimumProtocolVersion)
            {
                throw VersionMismatchException.HostIsTooOld(localVersion, hostInfo.Version);
            }

            // The host is the Stammdaten master (#183). Pulled on the same HttpClient as everything
            // else, so the PIN header and the Trust-on-First-Use certificate pin apply unchanged.
            // Deliberately not re-fetched on reconnect: the host caches its serialized set at
            // StartAsync, and both that cached copy and this client's workspace hold the same
            // MasterDataSet as an immutable value fixed at open — the Stammdaten editor stays
            // reachable throughout, but an edit made there produces a new value, it doesn't mutate
            // the one already handed out. A resync round trip here would buy nothing.
            var hostMasterDataJson = await http.GetStringAsync(
                new Uri(SyncProtocol.MasterDataPath, UriKind.RelativeOrAbsolute), ct);

            // Kept as the snapshot, not just the mapped Incident: its position seeds the session's
            // applied position, so the client starts level with the host instead of treating
            // everything up to the joined-at revision as new.
            var snapshot = SyncJson.Deserialize<IncidentSnapshot>(
                await http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute), ct));

            return new RemoteJoin(http, handler, baseUri, pin, snapshot, hostMasterDataJson);
        }
        catch
        {
            http.Dispose();
            handler.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Completes the join: opens the push channel and returns the live session, which from then on
    /// owns the connection. Catches up on anything the host changed since <see cref="OpenAsync"/>, so a
    /// Lagebuchführer who took a while to type their name still starts on the host's current state.
    /// Throws <see cref="InvalidOperationException"/> when this join was already connected or
    /// disposed, and <see cref="HttpRequestException"/> (or a SignalR failure) when the host went away
    /// in the meantime; either way the connection is released and a new join has to be opened. The
    /// other parameters are those of <see cref="RemoteIncidentSession.ConnectAsync"/>.
    /// </summary>
    public async Task<RemoteIncidentSession> ConnectAsync(
        SessionOperator op,
        IUiDispatcher ui,
        IRetryPolicy? reconnectPolicy = null,
        string? cacheRoot = null,
        long cacheMaxBytes = RemoteIncidentSession.DefaultCacheMaxBytes,
        TimeSpan? reconcileInterval = null,
        TimeProvider? timeProvider = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(op);
        ArgumentNullException.ThrowIfNull(ui);
        if (Interlocked.Exchange(ref _spent, 1) == 1)
        {
            throw new InvalidOperationException("This join was already connected or disposed.");
        }

        return await RemoteIncidentSession.StartAsync(
            _http,
            _handler,
            _baseUri,
            _pin,
            _snapshot,
            HostMasterDataJson,
            op,
            ui,
            reconnectPolicy,
            cacheRoot,
            cacheMaxBytes,
            reconcileInterval,
            timeProvider,
            ct);
    }

    /// <summary>
    /// Releases the connection unless <see cref="ConnectAsync"/> already handed it to a session.
    /// Idempotent.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _spent, 1) == 0)
        {
            _http.Dispose();
            _handler.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    // .NET wraps an exception thrown inside ServerCertificateCustomValidationCallback in an
    // HttpRequestException, keeping it as an inner cause rather than letting it propagate as-is; walk
    // the inner chain so the typed CertificateChangedException can be surfaced to the caller.
    private static TException? FindInner<TException>(Exception ex)
        where TException : Exception
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is TException t)
            {
                return t;
            }
        }

        return null;
    }
}
