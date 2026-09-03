using System.Net;
using System.Text;
using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

public class IncidentHostTests
{
    private static async Task<IncidentSnapshot> GetSnapshotAsync(HttpClient http) =>
        SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute)));

    [Fact]
    public async Task Host_serves_version_and_snapshot_and_applies_a_posted_command()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            new[] { ("Punkt A", false) },
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.2.3", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        // Version handshake.
        var version = SyncJson.Deserialize<VersionInfo>(await http.GetStringAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute)));
        Assert.Equal("1.2.3", version.Version);

        // Initial snapshot reflects the hosted incident.
        var before = await GetSnapshotAsync(http);
        Assert.DoesNotContain(before.Journal, e => e.Text == "Von der Einsatzstelle");

        // A client posts a command; the host applies it with the client's operator and the host clock.
        var command = new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"),
            EtbDirection.Incoming,
            "Von der Einsatzstelle",
            "Leitstelle",
            "ELW");
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        response.EnsureSuccessStatusCode();

        var after = await GetSnapshotAsync(http);
        var entry = Assert.Single(after.Journal, e => e.Text == "Von der Einsatzstelle");
        Assert.Equal("Client (RUF 1)", entry.EnteredBy); // attributed to the device, not the host
    }

    [Fact]
    public async Task Host_applies_an_edit_journal_entry_command_and_broadcasts_it()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.2.3", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var entry = session.Incident.AddJournalEntry(
            clock,
            new SessionOperator("Host", "FFB 1"),
            EtbDirection.Incoming,
            "Lagemeldung",
            "Leitstelle",
            "ELW");

        var command = new EditJournalEntryCommand(new OperatorDto("Client", "RUF 1"), entry.Id, "Lagemeldung korrigiert");
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        response.EnsureSuccessStatusCode();

        var after = await GetSnapshotAsync(http);
        var edited = Assert.Single(after.Journal, e => e.Id == entry.Id);
        Assert.Equal("Lagemeldung korrigiert", edited.Text);
        Assert.Equal("Client (RUF 1)", Assert.Single(edited.Edits).EditedBy);
    }

    [Fact]
    public async Task Host_rejects_an_edit_against_an_unknown_entry_id_with_400_not_500()
    {
        // KeyNotFoundException (EditJournalEntry against a stale or forged id) must land in the
        // same "reject cleanly" path as the other domain guards, not escape as an unhandled 500
        // (security review, #73).
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.2.3", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var command = new EditJournalEntryCommand(new OperatorDto("Client", null), Guid.NewGuid(), "Text");
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Host_rejects_a_command_against_a_closed_incident_with_400()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Close();
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");
        var command = new AddJournalEntryCommand(
            new OperatorDto("Client", null),
            EtbDirection.Internal,
            "zu spät",
            null,
            null);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");

        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null)] // no PIN header at all
    [InlineData("9999")] // wrong PIN
    public async Task Host_rejects_every_endpoint_and_the_hub_without_the_right_pin(string? pin)
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        if (pin is not null)
        {
            http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        }

        // The PIN gate refuses the first request with 401 — the documented auth response.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute))).StatusCode);

        // Every later request from the same IP returns 429: the gate still refuses an unauthenticated
        // client, and the brute-force guard (P0 #3) throttles the repeats — so no endpoint or the hub
        // is reachable without the right PIN.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute))).StatusCode);

        var command = new AddJournalEntryCommand(
            new OperatorDto("Client", null),
            EtbDirection.Internal,
            "x",
            null,
            null);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content)).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await http.PostAsync(new Uri(SyncProtocol.HubPath + "/negotiate?negotiateVersion=1", UriKind.RelativeOrAbsolute), null)).StatusCode);
    }

    [Fact]
    public async Task Client_uploads_a_file_and_can_pull_its_bytes_back()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var command = new AddFileCommand(new OperatorDto("Client", "RUF 1"), "brand.jpg", "image/jpeg", bytes);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var postResponse = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        postResponse.EnsureSuccessStatusCode();

        // Broadcast/response snapshot carries metadata only — no bytes, small regardless of file size.
        var snapshot = SyncJson.Deserialize<IncidentSnapshot>(await postResponse.Content.ReadAsStringAsync());
        var fileMeta = Assert.Single(snapshot.Files);
        Assert.Equal("brand.jpg", fileMeta.FileName);
        Assert.Equal("Client (RUF 1)", fileMeta.AddedBy);

        // Also lands in the host's own persisted state (attributed correctly, an ETB entry logged).
        Assert.Single(session.Incident.Files);
        Assert.Contains(session.Incident.Journal, e => e.Text == "Datei hinzugefügt: brand.jpg");

        // A client pulls the bytes back on demand.
        var getResponse = await http.GetAsync(new Uri(SyncProtocol.FilesPath(fileMeta.Id), UriKind.RelativeOrAbsolute));
        getResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", getResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await getResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Uploading_a_file_does_not_hold_the_UI_dispatcher_during_the_disk_write()
    {
        // issue #167 P1 #1: applying the domain mutation (needs the UI thread) and writing the
        // bytes (must not block it) are two separate _ui.InvokeAsync hops around a genuinely async
        // write — this proves the write happens between them, not inside either.
        var clock = new FixedClock();
        var gate = new TaskCompletionSource();
        var store = new DelayedFileWriteStore(gate.Task);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var dispatcher = new RecordingUiDispatcher();
        await using var host = new IncidentHost(session, clock, "1.0.0", dispatcher, "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var command = new AddFileCommand(new OperatorDto("Client", "RUF 1"), "brand.jpg", "image/jpeg", new byte[] { 1, 2, 3 });
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var postTask = http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);

        // Wait for the domain-mutation dispatch to run — it must complete even while the write
        // afterward is still stuck on the gate.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (dispatcher.InvokeCount < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Equal(1, dispatcher.InvokeCount);
        Assert.False(postTask.IsCompleted); // stuck on the byte write, not on a blocked UI thread

        gate.SetResult();
        var response = await postTask;
        response.EnsureSuccessStatusCode();

        Assert.Equal(2, dispatcher.InvokeCount); // domain mutation, then SaveExternalChange
    }

    [Fact]
    public async Task GetFile_returns_404_for_an_unknown_id()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var response = await http.GetAsync(new Uri(SyncProtocol.FilesPath(Guid.NewGuid()), UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFile_requires_the_pin_like_every_other_route()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };

        var response = await http.GetAsync(new Uri(SyncProtocol.FilesPath(Guid.NewGuid()), UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Host_accepts_the_hub_negotiate_with_the_right_pin()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var negotiate = await http.PostAsync(new Uri(SyncProtocol.HubPath + "/negotiate?negotiateVersion=1", UriKind.RelativeOrAbsolute), null);
        negotiate.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Host_serves_over_https_with_self_signed_cert()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");

        var response = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Repeated_wrong_pins_from_one_ip_trigger_429_with_retry_after()
    {
        var clock = new FixedClock();
        var session = LocalIncidentSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "9999");

        // First wrong PIN: 401 (no backoff yet).
        var first = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute));
        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);

        // Second wrong PIN from same IP: throttled with429 + Retry-After.
        var second = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute));
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.NotNull(second.Headers.RetryAfter);
        Assert.True(second.Headers.TryGetValues("Retry-After", out var retryValues));
        var retryAfterStr = Assert.Single(retryValues);
        Assert.True(int.TryParse(retryAfterStr, out var retryAfter) && retryAfter >= 1 && retryAfter <= 60);
    }
}
