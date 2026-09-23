using LageBuch.AppLogic;
using LageBuch.Domain;
using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// The other half of #295: a command the host refuses used to vanish. <c>Send</c> discarded the task
/// and <c>SendAsync</c> called <c>EnsureSuccessStatusCode</c>, so a 400 went into a dropped task —
/// the operator's input was silently lost, which at an ELW is indistinguishable from the screen being
/// stale. And the snapshot the host already returns on success was thrown away.
/// </summary>
public class CommandRejectionTests
{
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(10);

    private static LocalIncidentSession HostSession(FixedClock clock) => TestSession.StartNew(
        new InMemoryStore(),
        clock,
        new SessionOperator("Host", "FFB 1"),
        "/x.fwincident",
        Array.Empty<(string, bool)>(),
        Array.Empty<(string, bool)>());

    private static async Task<string> NextRejection(RemoteIncidentSession client, TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<string>();
        void Handler(string reason) => tcs.TrySetResult(reason);
        client.CommandRejected += Handler;
        try
        {
            return await tcs.Task.WaitAsync(timeout ?? TimeSpan.FromSeconds(5));
        }
        finally
        {
            client.CommandRejected -= Handler;
        }
    }

    [Fact]
    public async Task A_rejected_fire_and_forget_command_reaches_the_operator_with_the_hosts_reason()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        await using var host = new IncidentHost(hostSession, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(System.Net.IPAddress.Loopback, port);

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            "1234",
            port,
            reconcileInterval: Never);

        var rejection = NextRejection(client);

        // Void, fire-and-forget, like the ~40 mutation methods on this class. An unknown entry id is a
        // KeyNotFoundException at the host, i.e. a 400.
        client.EditJournalEntry(Guid.NewGuid(), "geht nicht");

        var reason = await rejection;

        // The host's own German sentence, unquoted -- the 400 body is a JSON string literal (see
        // IncidentHostTests.A_rejected_command_answers_with_the_hosts_reason_as_a_json_string), not the
        // HTTP status text the operator used to get nothing of.
        Assert.Contains("nicht gefunden", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("400", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_against_a_closed_incident_is_reported_with_the_domain_guards_words()
    {
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        await using var host = new IncidentHost(hostSession, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(System.Net.IPAddress.Loopback, port);

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            "1234",
            port,
            reconcileInterval: Never);

        hostSession.Close();

        var rejection = NextRejection(client);
        client.AddJournalEntry(EtbDirection.Incoming, "Zu spät");

        Assert.Equal("Der Einsatz ist abgeschlossen und schreibgeschützt.", await rejection);
    }

    [Fact]
    public async Task An_awaited_command_throws_instead_of_raising_the_event()
    {
        // AddFileAsync/RemoveFileAsync are awaited by FilesViewModel, which catches and shows the
        // message itself. They must keep throwing, and must not *also* raise CommandRejected, or the
        // operator gets the same failure twice in two places.
        var clock = new FixedClock();
        var hostSession = HostSession(clock);
        await using var host = new IncidentHost(hostSession, clock, "1.0.0", new ImmediateUiDispatcher(), "1234");
        var port = TestHost.FreeTcpPort();
        await host.StartAsync(System.Net.IPAddress.Loopback, port);

        await using var client = await RemoteIncidentSession.ConnectAsync(
            "127.0.0.1",
            new SessionOperator("Client", "RUF 1"),
            "1.0.0",
            new ImmediateUiDispatcher(),
            new InMemoryTrustStore(),
            "1234",
            port,
            reconcileInterval: Never);

        var raised = 0;
        client.CommandRejected += _ => Interlocked.Increment(ref raised);

        var rejected = await Assert.ThrowsAsync<CommandRejectedException>(
            () => client.RemoveFileAsync(Guid.NewGuid()));

        Assert.Contains("nicht gefunden", rejected.Message, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref raised));
    }

    [Fact]
    public async Task A_commands_own_response_brings_the_sender_up_to_date()
    {
        // Against a scripted host that answers with the fresh snapshot and pushes nothing at all, so the
        // only thing that can have updated the client is the response to its own command.
        var basis = SnapshotFixture.BaseSnapshot();
        await using var scripted = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(scripted, Never);

        scripted.Current = SnapshotFixture.Revised(basis, 1, "Vom Host übernommen");

        await client.SendAsync(new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Meldung", null, null));

        Assert.Equal(1, scripted.CommandsReceived);
        Assert.Equal(new[] { "Vom Host übernommen" }, SnapshotFixture.JournalOf(client));
    }

    [Fact]
    public async Task A_rejected_command_leaves_the_clients_state_alone()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var scripted = await ScriptedSnapshotHost.StartAsync(SnapshotFixture.Revised(basis, 2, "Unverändert"));
        await using var client = await SnapshotFixture.ConnectAsync(scripted, Never);

        scripted.RejectCommandsWith = "Punkt A ist unbekannt.";

        var rejection = NextRejection(client);
        client.AddJournalEntry(EtbDirection.Incoming, "Wird abgelehnt");

        Assert.Equal("Punkt A ist unbekannt.", await rejection);
        Assert.Equal(new[] { "Unverändert" }, SnapshotFixture.JournalOf(client));
    }
}
