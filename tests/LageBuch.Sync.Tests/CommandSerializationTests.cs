using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;

namespace LageBuch.Sync.Tests;

public class CommandSerializationTests
{
    private static readonly OperatorDto Op = new("Müller", "FFB 12/1");

    public static IEnumerable<object[]> AllCommands() => new SyncCommand[]
    {
        new AddJournalEntryCommand(Op, EtbDirection.Outgoing, "Text", "Von", "An"),
        new EditJournalEntryCommand(Op, Guid.NewGuid(), "Korrigierter Text"),
        new ChangeOperatorCommand(Op, new OperatorDto("Schmidt", null)),
        new ToggleChecklistItemCommand(Op, Guid.NewGuid()),
        new AssignRoleCommand(Op, "EL", "Huber", "FFB 1", DateTimeOffset.UnixEpoch, null, "Abschnitt", "0171"),
        new TransferRoleCommand(Op, Guid.NewGuid(), "Schmidt", "FFB 12/2", "0172"),
        new EditRolePhoneCommand(Op, Guid.NewGuid(), "0173"),
        new AssignRoleCommand(Op, "Abschnittsleiter", "Huber", null, null, null, null, null, Guid.NewGuid(), RoleSlot.Leiter),
        new AddCommandNodeCommand(Op, CommandNodeKind.Abschnitt, "Nord", null),
        new AddCommandNodeCommand(Op, CommandNodeKind.Unterabschnitt, "Nord 1", Guid.NewGuid()),
        new UpdateCommandNodeCommand(Op, Guid.NewGuid(), "Nord", "Brandbekämpfung", "Hauptstraße", "TG 2", "Florian Nord", "0171"),
        new DissolveCommandNodeCommand(Op, Guid.NewGuid()),
        new MoveForceUnitCommand(Op, Guid.NewGuid(), Guid.NewGuid()),
        new MoveForceUnitCommand(Op, Guid.NewGuid(), null),
        new PlaceRoleAssignmentCommand(Op, Guid.NewGuid(), Guid.NewGuid(), RoleSlot.Leiter),
        new PlaceRoleAssignmentCommand(Op, Guid.NewGuid(), null, RoleSlot.None),
        new SetFuehrungsstufeCommand(Op, Fuehrungsstufe.C),
        new SetFuehrungsstufeCommand(Op, null),
        new UpdateLeadershipPostCommand(Op, "Feuerwehrhaus", "Florian EL", "0172", "TG 1"),
        new SetInvolvedPartyDwellingCommand(Guid.NewGuid(), Guid.NewGuid()),
        new SetInvolvedPartyDwellingCommand(Guid.NewGuid(), null),
        new SetInvolvedPartyWhereaboutsCommand(Guid.NewGuid(), "im RTW Rotkreuz Musterstadt 71/1"),
        new SetInvolvedPartyWhereaboutsCommand(Guid.NewGuid(), null),
        new AddForceUnitCommand(Op, "Aich", 9, "Aich 42/1", "Im Einsatz", "Notiz", 4),
        new AddForceUnitCommand(Op, "Aich", 9, "Aich 42/1", "Im Einsatz", "Notiz", 4, 1),
        new UpdateForceUnitCommand(Op, Guid.NewGuid(), "Bereitstellung", null),
        new UpdateForceStrengthCommand(Op, Guid.NewGuid(), 1, 9, 4),
        new AddScbaTruppCommand(
            "Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1",
            "Menschenrettung",
            30,
            60,
            5,
            EntryPressure: 300),
        new AddScbaTruppCommand(
            "Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1",
            "Menschenrettung",
            30,
            60,
            5,
            EntryPressure: 300,
            TruppNumber: 3),
        new StartScbaTruppCommand(Guid.NewGuid()),
        new RecordScbaPressureCommand(Guid.NewGuid(), 250),
        new WithdrawScbaTruppCommand(Guid.NewGuid()),
        new MarkScbaRemovedCommand(Guid.NewGuid()),
        new SetScbaSafetyTruppCommand(Guid.NewGuid(), Guid.NewGuid()),
        new SetScbaSafetyTruppCommand(Guid.NewGuid(), null),
        new SetIncidentNumberCommand("B 1.2 260812 001"),
        new SetKeywordCommand("Brand 2"),
        new SetAddressCommand("Hauptstraße 1", "Bezirk 2"),
        new SetAddressCommand("Hauptstraße", "Puch", "1", "82256", "Fürstenfeldbruck"),
        new SetStatusCommand(null),
        new CloseIncidentCommand(Op),
        new AddFileCommand(Op, Guid.NewGuid(), "brand.jpg", "image/jpeg", 4),
        new RenameFileCommand(Guid.NewGuid(), "Küchenbrand"),
        new RenameFileCommand(Guid.NewGuid(), null),
        new AddTaskCommand(Op, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.Medium, 10),
        new AddTaskCommand(Op, "Nachfordern", string.Empty, TaskImportance.Low, TaskUrgency.Low, 30),
        new SetTaskCompletedCommand(Op, Guid.NewGuid(), true),
        new SetTaskCompletedCommand(Op, Guid.NewGuid(), false),
        new AddCoBuildingCommand(Op, "Haus A", 2, 3, 1),
        new UpdateCoBuildingStructureCommand(Op, Guid.NewGuid(), 3, 4, 1),
        new SetApartmentCountCommand(Op, Guid.NewGuid(), -1, 14),
        new RemoveDwellingsCommand(Op, Guid.NewGuid(), 0, new[] { 2, 4 }),
        new RemoveDwellingsCommand(Op, Guid.NewGuid(), -1, new[] { 7 }),
        new AddInvolvedPartyCommand(Op, "Erika Beispiel", "0171 0000001", "Hauseigentümerin"),
        new AddInvolvedPartyCommand(Op, "POK Mustermann", null, null),
        new UpdateInvolvedPartyCommand(Guid.NewGuid(), "Erika Beispiel", null, "Schlüssel übergeben"),
        new RemoveInvolvedPartyCommand(Guid.NewGuid()),
        new UpdateTaskCommand(Guid.NewGuid(), "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.Medium),
        new ExtendTaskTimerCommand(Guid.NewGuid(), 5),
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

    // The discriminator is a wire contract between joined devices, and this one carries a list --
    // the first command to do so on the CO side (#419).
    [Fact]
    public void RemoveDwellings_discriminator_and_its_list_survive_the_wire()
    {
        var buildingId = Guid.NewGuid();
        var json = SyncJson.Serialize<SyncCommand>(new RemoveDwellingsCommand(Op, buildingId, -1, new[] { 2, 4 }));
        Assert.Contains("\"$type\":\"removeDwellings\"", json, StringComparison.Ordinal);

        var back = Assert.IsType<RemoveDwellingsCommand>(SyncJson.Deserialize<SyncCommand>(json));
        Assert.Equal(buildingId, back.BuildingId);
        Assert.Equal(-1, back.FloorOrdinal);
        Assert.Equal(new[] { 2, 4 }, back.ApartmentNumbers);
    }

    [Fact]
    public void Discriminator_is_the_dollar_type_property()
    {
        var json = SyncJson.Serialize<SyncCommand>(new ToggleChecklistItemCommand(Op, Guid.NewGuid()));
        Assert.Contains("\"$type\":\"toggleChecklistItem\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SetScbaSafetyTrupp_discriminator_and_null_survive_the_wire()
    {
        // The discriminator is a wire contract: renaming it silently breaks a joined device, which
        // is only caught when a Sicherheitstrupp assignment stops arriving mid-Einsatz.
        var truppId = Guid.NewGuid();
        var json = SyncJson.Serialize<SyncCommand>(new SetScbaSafetyTruppCommand(truppId, null));
        Assert.Contains("\"$type\":\"setScbaSafetyTrupp\"", json, StringComparison.Ordinal);

        var back = Assert.IsType<SetScbaSafetyTruppCommand>(SyncJson.Deserialize<SyncCommand>(json));
        Assert.Equal(truppId, back.TruppId);
        Assert.Null(back.SafetyTruppId);
    }

    [Fact]
    public void AddFileCommand_carries_metadata_only_no_bytes()
    {
        // issue #167 P1 #2: the attachment's bytes never ride this command — only the id the client
        // generated up front (to correlate with the separate PUT upload) and the size it declares.
        var fileId = Guid.NewGuid();
        var json = SyncJson.Serialize<SyncCommand>(new AddFileCommand(Op, fileId, "brand.jpg", "image/jpeg", 5));

        var back = Assert.IsType<AddFileCommand>(SyncJson.Deserialize<SyncCommand>(json));

        Assert.Equal(fileId, back.FileId);
        Assert.Equal(5, back.SizeBytes);
        Assert.DoesNotContain("\"bytes\"", json, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("\"$type\":\"updateForceStrength\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveForceUnit_uses_the_removeForceUnit_discriminator_and_roundtrips()
    {
        var unitId = Guid.NewGuid();
        var json = SyncJson.Serialize<SyncCommand>(new RemoveForceUnitCommand(Op, unitId));
        Assert.Contains("\"$type\":\"removeForceUnit\"", json, StringComparison.Ordinal);

        var command = Assert.IsType<RemoveForceUnitCommand>(SyncJson.Deserialize<SyncCommand>(json));
        Assert.Equal(unitId, command.UnitId);
    }

    // The Beteiligte discriminators are a wire contract between joined devices: renaming one
    // silently breaks a peer on the same protocol.
    [Theory]
    [InlineData("addInvolvedParty")]
    [InlineData("updateInvolvedParty")]
    [InlineData("removeInvolvedParty")]
    public void Involved_party_commands_use_their_discriminators(string discriminator)
    {
        var id = Guid.NewGuid();
        SyncCommand command = discriminator switch
        {
            "addInvolvedParty" => new AddInvolvedPartyCommand(Op, "Erika Beispiel", null, null),
            "updateInvolvedParty" => new UpdateInvolvedPartyCommand(id, "Erika Beispiel", "110", null),
            _ => new RemoveInvolvedPartyCommand(id),
        };

        var json = SyncJson.Serialize(command);

        Assert.Contains($"\"$type\":\"{discriminator}\"", json, StringComparison.Ordinal);
        Assert.Equal(command.GetType(), SyncJson.Deserialize<SyncCommand>(json).GetType());
    }

    // A client at 7 to 9 sends both commands without the new ids: they still read, unlinked.
    [Fact]
    public void Legacy_addJournalEntry_and_addTask_without_the_new_ids_deserialize_as_null()
    {
        const string entryJson = """
            {"$type":"addJournalEntry","operator":{"name":"Müller","callSign":null},
             "direction":"Incoming","text":"Lage","from":"ILS","to":null}
            """;
        const string taskJson = """
            {"$type":"addTask","operator":{"name":"Müller","callSign":null},"text":"EVU informieren",
             "assignee":"","importance":"High","urgency":"High","timerMinutes":5}
            """;

        Assert.Null(Assert.IsType<AddJournalEntryCommand>(SyncJson.Deserialize<SyncCommand>(entryJson)).EntryId);
        Assert.Null(Assert.IsType<AddTaskCommand>(SyncJson.Deserialize<SyncCommand>(taskJson)).SourceEntryId);
    }

    [Fact]
    public void The_new_ids_survive_the_wire()
    {
        var entryId = Guid.NewGuid();
        var entry = SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize<SyncCommand>(
            new AddJournalEntryCommand(Op, EtbDirection.Incoming, "Lage", "ILS", null, entryId)));
        var task = SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize<SyncCommand>(
            new AddTaskCommand(Op, "EVU informieren", string.Empty, TaskImportance.High, TaskUrgency.High, 5, entryId)));

        Assert.Equal(entryId, Assert.IsType<AddJournalEntryCommand>(entry).EntryId);
        Assert.Equal(entryId, Assert.IsType<AddTaskCommand>(task).SourceEntryId);
    }

    [Fact]
    public void Add_command_node_round_trips_its_details_and_reads_a_payload_without_them()
    {
        var json = SyncJson.Serialize<SyncCommand>(new AddCommandNodeCommand(
            Op, CommandNodeKind.Abschnitt, "Nord", null, "Brandbekämpfung", "Hauptstraße", "TG 2", "Florian Nord", "0171"));
        var back = Assert.IsType<AddCommandNodeCommand>(SyncJson.Deserialize<SyncCommand>(json));
        Assert.Equal("Brandbekämpfung", back.Auftrag);
        Assert.Equal("Hauptstraße", back.Standort);
        Assert.Equal("TG 2", back.Funkgruppe);
        Assert.Equal("Florian Nord", back.Rufname);
        Assert.Equal("0171", back.Telefon);

        const string Old = "{\"$type\":\"addCommandNode\",\"operator\":{\"name\":\"Muster\",\"callSign\":\"FFB 1\"},\"kind\":\"Abschnitt\",\"name\":\"Nord\",\"parentId\":null}";
        var old = Assert.IsType<AddCommandNodeCommand>(SyncJson.Deserialize<SyncCommand>(Old));
        Assert.Equal("Nord", old.Name);
        Assert.Null(old.Auftrag);
        Assert.Null(old.Standort);
        Assert.Null(old.Funkgruppe);
        Assert.Null(old.Rufname);
        Assert.Null(old.Telefon);
    }

    [Fact]
    public void Placement_commands_use_their_discriminators_and_enums_travel_by_name()
    {
        var json = SyncJson.Serialize<SyncCommand>(new AddCommandNodeCommand(Op, CommandNodeKind.Bereitstellung, "BR", null));
        Assert.Contains("\"$type\":\"addCommandNode\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"Bereitstellung\"", json, StringComparison.Ordinal);

        Assert.Contains(
            "\"$type\":\"updateCommandNode\"",
            SyncJson.Serialize<SyncCommand>(new UpdateCommandNodeCommand(Op, Guid.NewGuid(), "N", null, null, null, null, null)),
            StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"dissolveCommandNode\"", SyncJson.Serialize<SyncCommand>(new DissolveCommandNodeCommand(Op, Guid.NewGuid())), StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"moveForceUnit\"", SyncJson.Serialize<SyncCommand>(new MoveForceUnitCommand(Op, Guid.NewGuid(), null)), StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"placeRoleAssignment\"", SyncJson.Serialize<SyncCommand>(new PlaceRoleAssignmentCommand(Op, Guid.NewGuid(), null, RoleSlot.Leiter)), StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"setFuehrungsstufe\"", SyncJson.Serialize<SyncCommand>(new SetFuehrungsstufeCommand(Op, Fuehrungsstufe.B)), StringComparison.Ordinal);
        Assert.Contains("\"$type\":\"updateLeadershipPost\"", SyncJson.Serialize<SyncCommand>(new UpdateLeadershipPostCommand(Op, null, null, null, null)), StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_assignRole_without_node_and_slot_deserializes_as_unplaced()
    {
        const string json = """{"$type":"assignRole","operator":{"name":"M","callSign":null},"role":"EL","personName":"H","callSign":null,"from":null,"to":null,"section":null,"phone":null}""";

        var c = Assert.IsType<AssignRoleCommand>(SyncJson.Deserialize<SyncCommand>(json));

        Assert.Null(c.NodeId);
        Assert.Equal(RoleSlot.None, c.Slot);
    }
}
