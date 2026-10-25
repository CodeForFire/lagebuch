using LageBuch.Domain.Etb;

namespace LageBuch.Sync.Hosting.Tests;

/// <summary>
/// One device's commands reach the host in the order they were made. They used to leave as
/// concurrent POSTs, each on whichever pooled connection was free, so a correction typed right after
/// its entry could overtake it: the host then refused the edit as "Eintrag nicht gefunden", and the
/// correction was lost. A load test found it in nearly every run with three devices or more.
/// </summary>
public class CommandOrderTests
{
    private static readonly TimeSpan Never = TimeSpan.FromMinutes(10);

    [Fact]
    public async Task A_device_sends_its_next_command_only_once_the_host_has_answered_the_last()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var scripted = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(scripted, Never);

        scripted.HoldCommands();
        var entryId = client.AddJournalEntry(EtbDirection.Incoming, "Lage erkundet");
        client.EditJournalEntry(entryId, "Lage erkundet, keine Personen");

        await scripted.WaitForHeldCommandAsync();

        // The edit must wait for the entry's answer. Before, it was on the wire a moment later, on a
        // second connection, racing the entry to the host.
        await Assert.ThrowsAsync<TimeoutException>(
            () => scripted.WaitForHeldCommandAsync(TimeSpan.FromMilliseconds(500)));

        scripted.ReleaseCommands(keepHolding: true);
        await scripted.WaitForHeldCommandAsync();
        scripted.ReleaseCommands();

        Assert.Collection(
            scripted.ReceivedCommands,
            first => Assert.IsType<AddJournalEntryCommand>(first),
            second => Assert.Equal(entryId, Assert.IsType<EditJournalEntryCommand>(second).EntryId));
    }

    [Fact]
    public async Task A_refused_command_does_not_hold_up_the_ones_after_it()
    {
        var basis = SnapshotFixture.BaseSnapshot();
        await using var scripted = await ScriptedSnapshotHost.StartAsync(basis);
        await using var client = await SnapshotFixture.ConnectAsync(scripted, Never);

        scripted.RejectCommandsWith = "Punkt A ist unbekannt.";
        await Assert.ThrowsAsync<CommandRejectedException>(() => client.SendAsync(new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Abgewiesen", null, null)));

        scripted.RejectCommandsWith = null;
        await client.SendAsync(new AddJournalEntryCommand(
            new OperatorDto("Client", "RUF 1"), EtbDirection.Incoming, "Danach", null, null));

        Assert.Equal(2, scripted.CommandsReceived);
    }
}
