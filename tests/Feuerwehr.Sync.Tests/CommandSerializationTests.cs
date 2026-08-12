using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Sync.Tests;

public class CommandSerializationTests
{
    private static readonly OperatorDto Op = new("Müller", "FFB 12/1");

    public static IEnumerable<object[]> AllCommands() => new SyncCommand[]
    {
        new AddJournalEntryCommand(Op, EtbDirection.Outgoing, "Text", "Von", "An"),
        new ToggleChecklistItemCommand(Guid.NewGuid()),
        new AssignRoleCommand("EL", "Huber", "FFB 1", DateTimeOffset.UnixEpoch, null, "Abschnitt", "0171"),
        new EndRoleAssignmentCommand(Guid.NewGuid()),
        new AddForceUnitCommand(Op, "Aich", 9, "Aich 42/1", "Im Einsatz", "Notiz", 4),
        new UpdateForceUnitCommand(Op, Guid.NewGuid(), "Bereitstellung", null),
        new AddScbaTruppCommand("Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1", "Menschenrettung", 30, 60, 5),
        new StartScbaTruppCommand(Guid.NewGuid(), 300),
        new RecordScbaPressureCommand(Guid.NewGuid(), 250),
        new MarkScbaReturnedCommand(Guid.NewGuid()),
        new SetIncidentNumberCommand("B 1.2 260812 001"),
        new SetKeywordCommand("Brand 2"),
        new SetAddressCommand("Hauptstraße 1", "Bezirk 2"),
        new SetStatusCommand(null),
        new CloseIncidentCommand(Op),
    }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllCommands))]
    public void Command_round_trips_through_the_polymorphic_base(SyncCommand command)
    {
        // Serialize as the base type so the $type discriminator is written; deserialize back as the
        // base and confirm the concrete type and every field survived.
        var json = SyncJson.Serialize(command);
        var back = SyncJson.Deserialize<SyncCommand>(json);

        Assert.Equal(command.GetType(), back.GetType());
        // Re-serialize (records with list members lack structural equality, so compare the wire form).
        Assert.Equal(json, SyncJson.Serialize(back));
    }

    [Fact]
    public void Discriminator_is_the_dollar_type_property()
    {
        var json = SyncJson.Serialize<SyncCommand>(new ToggleChecklistItemCommand(Guid.NewGuid()));
        Assert.Contains("\"$type\":\"toggleChecklistItem\"", json);
    }
}
