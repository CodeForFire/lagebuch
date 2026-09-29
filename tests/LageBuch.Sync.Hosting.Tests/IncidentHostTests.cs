using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Files;
using Microsoft.AspNetCore.Http;

namespace LageBuch.Sync.Hosting.Tests;

public class IncidentHostTests
{
    private static async Task<IncidentSnapshot> GetSnapshotAsync(HttpClient http) =>
        SyncJson.Deserialize<IncidentSnapshot>(await http.GetStringAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute)));

    private static async Task<long> GetRevisionAsync(HttpClient http) =>
        (await GetRevisionInfoAsync(http)).Revision;

    private static async Task<RevisionInfo> GetRevisionInfoAsync(HttpClient http) =>
        SyncJson.Deserialize<RevisionInfo>(await http.GetStringAsync(new Uri(SyncProtocol.RevisionPath, UriKind.RelativeOrAbsolute)));

    private static async Task PostAsync(HttpClient http, SyncCommand command)
    {
        using var content = new StringContent(SyncJson.Serialize(command), Encoding.UTF8, "application/json");
        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Host_serves_version_and_snapshot_and_applies_a_posted_command()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // Version handshake.
        var version = SyncJson.Deserialize<VersionInfo>(await http.GetStringAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute)));
        Assert.Equal("1.2.3", version.Version);
        Assert.Equal(SyncProtocol.ProtocolVersion, version.Protocol);
        Assert.Equal(SyncProtocol.MinimumProtocolVersion, version.MinProtocol);

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
    public async Task Host_bound_to_IPv6Any_still_accepts_IPv4_connections()
    {
        // Proves the dual-stack path (StartAsync -> ListenAnyIP for a wildcard address): a socket
        // bound only to IPv6Any accepts an IPv4 connection solely because DualMode is on. Asserting
        // via 127.0.0.1 rather than ::1 keeps this test independent of whether the CI/sandbox
        // network namespace has IPv6 loopback configured at all -- IPv4 loopback always is.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.2.3", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.IPv6Any, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var version = SyncJson.Deserialize<VersionInfo>(await http.GetStringAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute)));
        Assert.Equal("1.2.3", version.Version);
    }

    [Fact]
    public async Task Host_applies_an_edit_journal_entry_command_and_broadcasts_it()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var command = new EditJournalEntryCommand(new OperatorDto("Client", null), Guid.NewGuid(), "Text");
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Host_rejects_a_command_against_a_closed_incident_with_400()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    [Fact]
    public async Task Host_refuses_a_close_command_from_a_client_with_403_and_the_incident_stays_open()
    {
        // #465: any joined device could permanently close the host's incident. An older client
        // still sends CloseIncidentCommand, so the host has to refuse it, not just the new UI.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var command = new CloseIncidentCommand(new OperatorDto("Client", null));
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");

        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(IncidentState.Open, session.Incident.State);
    }

    [Theory]
    [InlineData(null)] // no PIN header at all
    [InlineData("9999")] // wrong PIN
    public async Task Host_rejects_every_endpoint_and_the_hub_without_the_right_pin(string? pin)
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
            http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // The PIN gate refuses the first request with 401 — the documented auth response.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute))).StatusCode);

        // So does every other endpoint and the hub: no route is reachable without the right PIN.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute))).StatusCode);

        var command = new AddJournalEntryCommand(
            new OperatorDto("Client", null),
            EtbDirection.Internal,
            "x",
            null,
            null);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync(new Uri(SyncProtocol.HubPath + "/negotiate?negotiateVersion=1", UriKind.RelativeOrAbsolute), null)).StatusCode);
    }

    [Fact]
    public async Task Client_registers_a_file_then_uploads_its_bytes_and_can_pull_them_back()
    {
        // issue #167 P1 #2: upload is now two requests — a small metadata command, then a raw-byte
        // PUT keyed by the id the client generated for it — rather than the bytes riding the command
        // as a base64-inflated JSON blob.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var fileId = Guid.NewGuid();
        var command = new AddFileCommand(new OperatorDto("Client", "RUF 1"), fileId, "brand.jpg", "image/jpeg", bytes.LongLength);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var postResponse = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        postResponse.EnsureSuccessStatusCode();

        // Broadcast/response snapshot carries metadata only — no bytes, small regardless of file size.
        var snapshot = SyncJson.Deserialize<IncidentSnapshot>(await postResponse.Content.ReadAsStringAsync());
        var fileMeta = Assert.Single(snapshot.Files);
        Assert.Equal(fileId, fileMeta.Id);
        Assert.Equal("brand.jpg", fileMeta.FileName);
        Assert.Equal("Client (RUF 1)", fileMeta.AddedBy);

        // Also lands in the host's own persisted state (attributed correctly, an ETB entry logged) —
        // even before any bytes have arrived.
        Assert.Single(session.Incident.Files);
        Assert.Contains(session.Incident.Journal, e => e.Text == "Datei hinzugefügt: brand.jpg");

        // The client PUTs the raw bytes next, keyed by the same id.
        using var uploadContent = new ByteArrayContent(bytes);
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var putResponse = await http.PutAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), uploadContent);
        putResponse.EnsureSuccessStatusCode();

        // A client pulls the bytes back on demand.
        var getResponse = await http.GetAsync(new Uri(SyncProtocol.FilesPath(fileMeta.Id), UriKind.RelativeOrAbsolute));
        getResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/jpeg", getResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(bytes, await getResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Removing_a_file_deletes_its_bytes_so_a_later_get_404s()
    {
        // issue #262 UX follow-up: full round trip mirroring the register-then-upload-then-pull
        // test above, but ending with a RemoveFileCommand — proves the metadata AND the bytes are
        // both gone, not just the incident_files row.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var fileId = Guid.NewGuid();
        var addCommand = new AddFileCommand(new OperatorDto("Client", "RUF 1"), fileId, "brand.jpg", "image/jpeg", bytes.LongLength);
        var addContent = new StringContent(SyncJson.Serialize<SyncCommand>(addCommand), Encoding.UTF8, "application/json");
        (await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), addContent)).EnsureSuccessStatusCode();

        using var uploadContent = new ByteArrayContent(bytes);
        uploadContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        (await http.PutAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), uploadContent)).EnsureSuccessStatusCode();

        // Bytes are pullable before removal.
        (await http.GetAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute))).EnsureSuccessStatusCode();

        var removeCommand = new RemoveFileCommand(new OperatorDto("Client", "RUF 1"), fileId);
        var removeContent = new StringContent(SyncJson.Serialize<SyncCommand>(removeCommand), Encoding.UTF8, "application/json");
        var removeResponse = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), removeContent);
        removeResponse.EnsureSuccessStatusCode();

        var snapshot = SyncJson.Deserialize<IncidentSnapshot>(await removeResponse.Content.ReadAsStringAsync());
        Assert.Empty(snapshot.Files);
        Assert.Contains(session.Incident.Journal, e => e.Text == "Datei entfernt: brand.jpg");

        var afterGet = await http.GetAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute));
        Assert.Equal(HttpStatusCode.NotFound, afterGet.StatusCode);
    }

    [Fact]
    public async Task UploadFile_returns_404_for_a_file_id_never_registered_via_a_command()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using var uploadContent = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var response = await http.PutAsync(new Uri(SyncProtocol.FilesPath(Guid.NewGuid()), UriKind.RelativeOrAbsolute), uploadContent);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadFile_rejects_a_body_over_the_cap()
    {
        // The server-side cap is independent of the metadata command's own (client-declared)
        // SizeBytes — a lying/buggy client's PUT is still rejected. A real over-cap payload (rather
        // than a length-only trick) exercises the same Content-Length fast-path HttpClient itself
        // requires the sent byte count to match, so this is also the most realistic reproduction —
        // and 25 MB over loopback is still a sub-second transfer.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var fileId = Guid.NewGuid();
        var command = new AddFileCommand(new OperatorDto("Client", "RUF 1"), fileId, "brand.jpg", "image/jpeg", 3);
        var addContent = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        (await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), addContent)).EnsureSuccessStatusCode();

        using var oversizedContent = new ByteArrayContent(new byte[IncidentFile.MaxSizeBytes + 1]);
        var response = await http.PutAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), oversizedContent);

        Assert.Equal((HttpStatusCode)StatusCodes.Status413PayloadTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_files_bytes_never_touches_the_ui_dispatcher()
    {
        // issue #167 P1 #2: bytes now arrive via a standalone PUT (HandleUploadFile) rather than
        // inline inside HandleCommand — like HandleGetFile, it's a pure disk write against
        // already-registered domain state, so it never needs the UI-thread dispatch HandleCommand's
        // metadata mutation still uses.
        var clock = new FixedClock();
        var gate = new TaskCompletionSource();
        var store = new DelayedFileWriteStore(gate.Task);
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var fileId = Guid.NewGuid();
        var command = new AddFileCommand(new OperatorDto("Client", "RUF 1"), fileId, "brand.jpg", "image/jpeg", 3);
        var content = new StringContent(SyncJson.Serialize<SyncCommand>(command), Encoding.UTF8, "application/json");
        var postResponse = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        postResponse.EnsureSuccessStatusCode();

        Assert.Equal(2, dispatcher.InvokeCount); // domain mutation, then SaveExternalChange — metadata only

        using var uploadContent = new ByteArrayContent(new byte[] { 1, 2, 3 });
        var putTask = http.PutAsync(new Uri(SyncProtocol.FilesPath(fileId), UriKind.RelativeOrAbsolute), uploadContent);

        // The PUT is stuck on the gated write — but it never touched the UI dispatcher to get there.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!putTask.IsCompleted && DateTime.UtcNow < deadline && dispatcher.InvokeCount < 3)
        {
            await Task.Delay(10);
        }

        Assert.False(putTask.IsCompleted);
        Assert.Equal(2, dispatcher.InvokeCount); // unchanged — HandleUploadFile never calls _ui.InvokeAsync

        gate.SetResult();
        var putResponse = await putTask;
        putResponse.EnsureSuccessStatusCode();
        Assert.Equal(2, dispatcher.InvokeCount);
    }

    [Fact]
    public async Task GetFile_returns_404_for_an_unknown_id()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var response = await http.GetAsync(new Uri(SyncProtocol.FilesPath(Guid.NewGuid()), UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetFile_requires_the_pin_like_every_other_route()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var negotiate = await http.PostAsync(new Uri(SyncProtocol.HubPath + "/negotiate?negotiateVersion=1", UriKind.RelativeOrAbsolute), null);
        negotiate.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Host_still_serves_the_version_endpoint_to_a_peer_it_would_otherwise_refuse()
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: 5);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "1");

        // The exemption: a refused peer has to be able to read the range it failed, or it can never
        // tell the Lagebuchführer which of the two devices to update.
        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute));

        response.EnsureSuccessStatusCode();
        var version = SyncJson.Deserialize<VersionInfo>(await response.Content.ReadAsStringAsync());
        Assert.Equal(5, version.MinProtocol);
    }

    [Theory]
    [InlineData(SyncProtocol.SnapshotPath)]
    [InlineData(SyncProtocol.RevisionPath)]
    [InlineData(SyncProtocol.MasterDataPath)]
    [InlineData(SyncProtocol.CommandPath)]
    public async Task Host_refuses_every_endpoint_below_its_minimum_protocol(string path)
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: 5);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "4");

        var response = await gated.Http.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
        Assert.Contains("aktualisieren", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Host_refuses_the_hub_negotiate_below_its_minimum_protocol()
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: 5);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "4");

        var negotiate = await gated.Http.PostAsync(new Uri(SyncProtocol.HubPath + "/negotiate?negotiateVersion=1", UriKind.RelativeOrAbsolute), null);

        Assert.Equal(HttpStatusCode.UpgradeRequired, negotiate.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.0")]
    [InlineData("-1")]
    public async Task Host_refuses_a_protocol_header_it_cannot_read(string claimed)
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: SyncProtocol.MinimumProtocolVersion);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, claimed);

        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task Host_refuses_a_duplicated_protocol_header()
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: SyncProtocol.MinimumProtocolVersion);

        // Two values coalesce into one header; refused rather than silently picking one, the same
        // rule PinMatches applies.
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "99");

        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task Host_accepts_a_request_with_no_protocol_header_while_its_floor_includes_the_legacy_protocol()
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: SyncProtocol.LegacyProtocolVersion);

        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        response.EnsureSuccessStatusCode();
    }

    // SyncProtocol's doc comment claims the v0.6.1 contract *is* protocol 1, and nothing else states
    // it. The refusal test above cannot hold it: that one only fails when the number rises to the
    // floor, and 0 is what an absent member on the wire lands on -- a drift down to it would leave
    // every other test green while making the doc comment false and the 0 -> Legacy mapping in
    // RemoteIncidentSession a no-op.
    [Fact]
    public void Legacy_protocol_is_the_contract_a_pre_handshake_build_speaks()
    {
        Assert.Equal(1, SyncProtocol.LegacyProtocolVersion);
    }

    [Fact]
    public async Task Host_refuses_a_request_with_no_protocol_header_once_its_floor_is_above_the_legacy_protocol()
    {
        // A v0.6.1 client sends no header and cannot speak the Beteiligte commands (protocol 3); it
        // is gated as the legacy number it is, not waved through for having said nothing.
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: SyncProtocol.MinimumProtocolVersion);

        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task Host_accepts_a_peer_claiming_a_newer_protocol_than_it_speaks()
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: SyncProtocol.MinimumProtocolVersion);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "99");

        // The host's gate is one-sided on purpose. A peer claiming something newer has read /version,
        // seen this host's ceiling and chosen to speak down to it; refusing it here would 426 a client
        // its own handshake had just approved, one request after approving it.
        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task A_protocol_mismatch_is_refused_after_the_pin_not_before()
    {
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: 5, pin: "1234");
        gated.Http.DefaultRequestHeaders.Remove(SyncProtocol.PinHeader);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "9999");
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "1");

        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        // Auth precedes content: a peer that cannot get in never learns which protocols this host speaks.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // A started host whose protocol floor the test chooses, plus a PIN-carrying client onto it. The
    // floor is overridable for tests only: at its real value of SyncProtocol.MinimumProtocolVersion
    // no legitimately-too-old peer can be constructed, which would leave the 426 path shipping
    // untested until the first release that raises it.
    private static async Task<ProtocolGatedHost> ProtocolGatedHostAsync(int minimumProtocolVersion, string pin = "1234")
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var host = new IncidentHost(
            session,
            clock,
            "1.0.0",
            new ImmediateUiDispatcher(),
            pin,
            null,
            Math.Max(minimumProtocolVersion, SyncProtocol.ProtocolVersion),
            minimumProtocolVersion);
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        return new ProtocolGatedHost(host, http);
    }

    // Owns both the Kestrel host and the client dialling it, so one `await using` in each test above
    // shuts the listener down rather than leaving one per test for the run's duration.
    private sealed class ProtocolGatedHost : IAsyncDisposable
    {
        private readonly IncidentHost _host;

        public ProtocolGatedHost(IncidentHost host, HttpClient http)
        {
            _host = host;
            Http = http;
        }

        public HttpClient Http { get; }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Host_serves_over_https_with_self_signed_cert()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var response = await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.RelativeOrAbsolute));
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Revision_starts_at_zero_and_advances_once_per_applied_change()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(0, await GetRevisionAsync(http));

        await PostAsync(http, new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Erste Meldung", null, null));
        Assert.Equal(1, await GetRevisionAsync(http));

        await PostAsync(http, new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Zweite Meldung", null, null));
        Assert.Equal(2, await GetRevisionAsync(http));

        // A host-side edit travels the same Changed -> OnSessionChanged path, so it counts too.
        session.SetKeyword("B3P");
        Assert.Equal(3, await GetRevisionAsync(http));
    }

    [Fact]
    public async Task GET_snapshot_carries_the_same_revision_as_GET_revision()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await PostAsync(http, new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Meldung", null, null));

        // The pair a joined client compares. If these two could disagree, a client would record a
        // position it does not hold the content for and then discard the broadcast that fixes it.
        var position = await GetRevisionInfoAsync(http);
        var snapshot = await GetSnapshotAsync(http);
        Assert.Equal(position.Revision, snapshot.Revision);
        Assert.Equal(position.Epoch, snapshot.Epoch);
        Assert.NotEqual(Guid.Empty, snapshot.Epoch);
    }

    [Theory]
    [InlineData(SyncProtocol.SnapshotPath)]
    [InlineData(SyncProtocol.RevisionPath)]
    public async Task A_read_of_the_incident_goes_through_the_ui_dispatcher(string path)
    {
        // Regression: GET /snapshot used to read _session.Incident straight from a Kestrel thread
        // while the UI thread mutated it. Both reads now hop onto the UI thread, as /command does —
        // which is also what keeps the served position and content one consistent pair.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        using var ui = new SerialUiDispatcher();
        await using var host = new IncidentHost(session, clock, "1.0.0", ui, "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);

        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var before = ui.Invocations;
        (await http.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute))).EnsureSuccessStatusCode();

        Assert.Equal(before + 1, ui.Invocations);
    }

    [Fact]
    public async Task Sharing_again_starts_a_new_epoch()
    {
        // What lets a client tell a restarted host's revision 3 from the revision 3 it already holds.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await using var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");

        async Task<Guid> ShareAndReadEpochAsync()
        {
            var port = TestHost.FreeTcpPort();
            await host.StartAsync(IPAddress.Loopback, port);
            using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
            http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, "1234");
            http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var epoch = (await GetRevisionInfoAsync(http)).Epoch;
            await host.StopAsync();
            return epoch;
        }

        var first = await ShareAndReadEpochAsync();
        var second = await ShareAndReadEpochAsync();

        Assert.NotEqual(Guid.Empty, first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Host_refuses_a_protocol_3_client()
    {
        // Protocol 4 raised the floor (#295). A protocol-3 client would not break against this host
        // itself, but the floor is one number for both directions, and a 4 client cannot use a 3 host.
        await using var gated = await ProtocolGatedHostAsync(minimumProtocolVersion: SyncProtocol.MinimumProtocolVersion);
        gated.Http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, "3");

        var response = await gated.Http.GetAsync(new Uri(SyncProtocol.SnapshotPath, UriKind.RelativeOrAbsolute));

        Assert.Equal(HttpStatusCode.UpgradeRequired, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_command_does_not_advance_the_revision()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // An unknown entry id is a 400 (KeyNotFoundException), so nothing was applied.
        using var content = new StringContent(
            SyncJson.Serialize<SyncCommand>(new EditJournalEntryCommand(new OperatorDto("Client", "RUF 1"), Guid.NewGuid(), "x")),
            Encoding.UTF8,
            "application/json");
        var rejected = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        Assert.Equal(0, await GetRevisionAsync(http));
    }

    [Fact]
    public async Task GET_revision_requires_the_pin()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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

        var response = await http.GetAsync(new Uri(SyncProtocol.RevisionPath, UriKind.RelativeOrAbsolute));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_command_answers_with_the_hosts_reason_as_a_json_string()
    {
        // Pins the shape the client has to parse. Results.BadRequest(string) goes through
        // TypedResults.BadRequest<T> and WriteResultAsJsonAsync, so the reason arrives as a JSON string
        // *literal* -- quotes and all, application/json -- not as text/plain. The umlaut is part of the
        // assertion so a later switch to Results.Content cannot silently mojibake a German message.
        var clock = new FixedClock();
        var session = TestSession.StartNew(
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
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

        session.Close();

        using var content = new StringContent(
            SyncJson.Serialize<SyncCommand>(new AddJournalEntryCommand(
                new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Zu spät", null, null)),
            Encoding.UTF8,
            "application/json");
        var response = await http.PostAsync(new Uri(SyncProtocol.CommandPath, UriKind.RelativeOrAbsolute), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "\"Der Einsatz ist abgeschlossen und schreibgeschützt.\"",
            await response.Content.ReadAsStringAsync());
    }

    private static async Task<(IncidentHost Host, int Port)> StartGatedHostAsync()
    {
        var clock = new FixedClock();
        var session = TestSession.StartNew(
            new InMemoryStore(),
            clock,
            new SessionOperator("Host", "FFB 1"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var host = new IncidentHost(session, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(IPAddress.Loopback, port);
        return (host, port);
    }

    private static async Task<HttpStatusCode> GetVersionWithPinAsync(int port, string pin)
    {
        using var http = new HttpClient(TestHost.InsecureTrustAllHandler()) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
        http.DefaultRequestHeaders.Add(SyncProtocol.PinHeader, pin);
        http.DefaultRequestHeaders.Add(SyncProtocol.ProtocolHeader, SyncProtocol.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
        return (await http.GetAsync(new Uri(SyncProtocol.VersionPath, UriKind.Relative))).StatusCode;
    }

    // #288: the attack replayed end to end, at the pace a person retries. Every wrong PIN is a 401
    // and every one counts, so "ten wrong PINs" means the same to the Lagebuchführer as to the
    // gate: the tenth closes joins, and from then on even the right PIN is refused to anyone who
    // has not joined yet.
    [Fact]
    public async Task Ten_wrong_pins_close_joins_and_then_even_the_right_pin_is_refused()
    {
        var (host, port) = await StartGatedHostAsync();
        await using var _ = host;
        var raised = 0;
        host.JoinsClosedChanged += (_, _) => raised++;

        for (var i = 0; i < JoinGate.MaxFailuresPerPin; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await GetVersionWithPinAsync(port, "0000"));
        }

        Assert.True(host.JoinsClosed);
        Assert.Equal(1, raised);
        Assert.Equal(HttpStatusCode.Unauthorized, await GetVersionWithPinAsync(port, "1234"));
    }

    [Fact]
    public async Task After_a_new_pin_new_joins_use_it_and_a_device_joined_earlier_keeps_its_pin()
    {
        var (host, port) = await StartGatedHostAsync();
        await using var _ = host;
        Assert.Equal(HttpStatusCode.OK, await GetVersionWithPinAsync(port, "1234"));
        for (var i = 0; i < JoinGate.MaxFailuresPerPin; i++)
        {
            await GetVersionWithPinAsync(port, "0000");
        }

        host.ReplacePin("5678");

        Assert.False(host.JoinsClosed);
        Assert.Equal(HttpStatusCode.OK, await GetVersionWithPinAsync(port, "5678"));
        Assert.Equal(HttpStatusCode.OK, await GetVersionWithPinAsync(port, "1234"));
    }
}
