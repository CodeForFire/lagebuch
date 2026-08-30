using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;

namespace LageBuch.Sync.Tests;

public class CommandSerializationTests
{
    private static readonly OperatorDto Op = new("Müller", "FFB 12/1");

    public static IEnumerable<object[]> AllCommands() => new SyncCommand[]
    {
        new AddJournalEntryCommand(Op, EtbDirection.Outgoing, "Text", "Von", "An"),
        new EditJournalEntryCommand(Op, Guid.NewGuid(), "Korrigierter Text"),
        new ToggleChecklistItemCommand(Op, Guid.NewGuid()),
        new AssignRoleCommand(Op, "EL", "Huber", "FFB 1", DateTimeOffset.UnixEpoch, null, "Abschnitt", "0171"),
        new TransferRoleCommand(Op, Guid.NewGuid(), "Schmidt", "FFB 12/2", "0172"),
        new EditRolePhoneCommand(Op, Guid.NewGuid(), "0173"),
        new AddForceUnitCommand(Op, "Aich", 9, "Aich 42/1", "Im Einsatz", "Notiz", 4),
        new AddForceUnitCommand(Op, "Aich", 9, "Aich 42/1", "Im Einsatz", "Notiz", 4, 1),
        new UpdateForceUnitCommand(Op, Guid.NewGuid(), "Bereitstellung", null),
        new UpdateForceStrengthCommand(Op, Guid.NewGuid(), 1, 9, 4),
        new AddScbaTruppCommand("Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1", "Menschenrettung", 30, 60, 5, EntryPressure: 300),
        new AddScbaTruppCommand("Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1", "Menschenrettung", 30, 60, 5, EntryPressure: 300, TruppNumber: 3),
        new StartScbaTruppCommand(Guid.NewGuid()),
        new RecordScbaPressureCommand(Guid.NewGuid(), 250),
        new WithdrawScbaTruppCommand(Guid.NewGuid()),
        new MarkScbaRemovedCommand(Guid.NewGuid()),
        new SetIncidentNumberCommand("B 1.2 260812 001"),
        new SetKeywordCommand("Brand 2"),
        new SetAddressCommand("Hauptstraße 1", "Bezirk 2"),
        new SetStatusCommand(null),
        new CloseIncidentCommand(Op),
        new AddFileCommand(Op, "brand.jpg", "image/jpeg", new byte[] { 1, 2, 3, 4 }),
        new RenameFileCommand(Guid.NewGuid(), "Küchenbrand"),
        new RenameFileCommand(Guid.NewGuid(), null),
        new AddTaskCommand(Op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.Medium, 10),
        new AddTaskCommand(Op, "Nachfordern", "", TaskImportance.Low, TaskUrgency.Low, 30),
        new SetTaskCompletedCommand(Op, Guid.NewGuid(), true),
        new SetTaskCompletedCommand(Op, Guid.NewGuid(), false),
    }.Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(AllCommands))]
    public void Command_round_trips_through_the_polymorphic_base(SyncCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
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
        var json = SyncJson.Serialize<SyncCommand>(new ToggleChecklistItemCommand(Op, Guid.NewGuid()));
        Assert.Contains("\"$type\":\"toggleChecklistItem\"", json);
    }

    [Fact]
    public void AddFileCommand_bytes_base64_round_trip_exactly()
    {
        var bytes = new byte[] { 0, 1, 254, 255, 42 };
        var json = SyncJson.Serialize<SyncCommand>(new AddFileCommand(Op, "brand.jpg", "image/jpeg", bytes));

        var back = Assert.IsType<AddFileCommand>(SyncJson.Deserialize<SyncCommand>(json));

        Assert.Equal(bytes, back.Bytes);
    }

    // A pre-#76 host or client sends addForceUnit without OfficerCount; the missing property must
    // deserialize as "keine Führungskraft erfasst" (0) rather than fail the wire contract.
    [Fact]
    public void Legacy_addForceUnit_without_officerCount_deserializes_as_zero()
    {
        const string legacyJson = """
            {"$type":"addForceUnit","operator":{"name":"Müller","callSign":"FFB 12/1"},
             "brigade":"Aich","personnelCount":9,"callSign":"Aich 42/1","status":null,
             "notes":null,"scbaCount":4}
            """;

        var command = Assert.IsType<AddForceUnitCommand>(SyncJson.Deserialize<SyncCommand>(legacyJson));

        Assert.Equal(0, command.OfficerCount);
        Assert.Equal(9, command.PersonnelCount);
    }

    // TruppNumber is optional on the wire so the host's auto-assign path (Incident.NextFreeScbaTruppNumber)
    // stays reachable from a client that never sent one.
    [Fact]
    public void AddScbaTrupp_without_truppNumber_deserializes_as_null()
    {
        const string json = """
            {"$type":"addScbaTrupp","designation":"Angriffstrupp",
             "members":[{"role":"Truppfuehrer","name":"Müller"}],
             "callSign":"AT-1","task":null,"maxDurationMinutes":30,"returnPressureBar":60,
             "pressureControlIntervalMinutes":5,"entryPressure":300}
            """;

        var command = Assert.IsType<AddScbaTruppCommand>(SyncJson.Deserialize<SyncCommand>(json));

        Assert.Null(command.TruppNumber);
        Assert.Equal(300, command.EntryPressure);
    }

    [Fact]
    public void UpdateForceStrength_uses_the_updateForceStrength_discriminator()
    {
        var json = SyncJson.Serialize<SyncCommand>(new UpdateForceStrengthCommand(Op, Guid.NewGuid(), 1, 9, 4));
        Assert.Contains("\"$type\":\"updateForceStrength\"", json);
    }

    [Fact]
    public void RemoveForceUnit_uses_the_removeForceUnit_discriminator_and_roundtrips()
    {
        var unitId = Guid.NewGuid();
        var json = SyncJson.Serialize<SyncCommand>(new RemoveForceUnitCommand(Op, unitId));
        Assert.Contains("\"$type\":\"removeForceUnit\"", json);

        var command = Assert.IsType<RemoveForceUnitCommand>(SyncJson.Deserialize<SyncCommand>(json));
        Assert.Equal(unitId, command.UnitId);
    }
}
