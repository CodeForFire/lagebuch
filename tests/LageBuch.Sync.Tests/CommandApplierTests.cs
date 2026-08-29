using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Tasks;

namespace LageBuch.Sync.Tests;

public class CommandApplierTests
{
    private static Incident NewIncident(FixedClock clock) =>
        Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null);

    private static void ApplyOverWire(SyncCommand command, Incident incident, FixedClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);

    [Fact]
    public void Journal_entry_is_attributed_to_the_commands_operator_not_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(new AddJournalEntryCommand(new OperatorDto("Client", "RUF 1"),
            EtbDirection.Incoming, "Meldung von der Einsatzstelle", "Leitstelle", "ELW"), incident, clock);

        var entry = incident.Journal.Last();
        Assert.Equal("Meldung von der Einsatzstelle", entry.Text);
        Assert.Equal("Client (RUF 1)", entry.EnteredBy); // the device's operator, not "Host (FFB 1)"
    }

    [Fact]
    public void Removing_a_unit_over_the_wire_takes_it_and_its_history_out()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new OperatorDto("Client", "RUF 1");
        ApplyOverWire(new AddForceUnitCommand(op, "Aich", 9, "Aich 42/1", null, null, 4, 1), incident, clock);
        var unitId = incident.Forces[0].Id;
        ApplyOverWire(new UpdateForceStrengthCommand(op, unitId, 2, 12, 6), incident, clock);
        var before = incident.Journal.Count;

        ApplyOverWire(new RemoveForceUnitCommand(op, unitId), incident, clock);

        Assert.Empty(incident.Forces);
        Assert.Equal((0, 0), (incident.TotalPersonnel, incident.TotalScba));
        var entry = incident.Journal.Last();
        Assert.Equal(before + 1, incident.Journal.Count);
        Assert.Equal("Einheit entfernt: Aich (Aich 42/1)", entry.Text);
        Assert.Equal("Client (RUF 1)", entry.EnteredBy);
    }

    [Fact]
    public void Applying_a_sequence_of_commands_converges_the_incident()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        incident.SeedChecklist(new[] { ("Punkt A", false), ("Punkt B", false) }, Array.Empty<(string, bool)>());
        var op = new OperatorDto("Client", "RUF 1");

        ApplyOverWire(new ToggleChecklistItemCommand(op, incident.ChecklistAufbau[0].Id), incident, clock);
        ApplyOverWire(new AssignRoleCommand(op, "EL", "Huber", "FFB 1", clock.Now, null, null, null), incident, clock);
        ApplyOverWire(new AddForceUnitCommand(op, "Aich", 9, "Aich 42/1", "Im Einsatz", null, 4, 1), incident, clock);
        ApplyOverWire(new UpdateForceStrengthCommand(op, incident.Forces[0].Id, 2, 12, 6), incident, clock);
        ApplyOverWire(new AddScbaTruppCommand("Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1", null, 30, 60, 5, EntryPressure: 300), incident, clock);
        ApplyOverWire(new StartScbaTruppCommand(incident.ScbaTrupps.Last().Id), incident, clock);

        Assert.True(incident.ChecklistAufbau[0].IsDone);
        Assert.Single(incident.Roles);
        Assert.Equal(12, incident.TotalPersonnel);
        // The strength correction arrived over the wire with officer count and edit trail intact.
        var corrected = incident.Forces[0];
        Assert.Equal((2, 12, 6, 1), (corrected.OfficerCount, corrected.PersonnelCount, corrected.ScbaCount,
            incident.Journal.Count(e => e.Text.Contains('→'))));
        var trupp = Assert.Single(incident.ScbaTrupps);
        Assert.Equal(300, trupp.EntryPressure);
    }

    [Fact]
    public void AddFileCommand_records_metadata_and_invokes_the_byte_writer()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var saved = new List<(string StorageFileName, byte[] Bytes)>();
        var bytes = new byte[] { 1, 2, 3 };

        CommandApplier.Apply(new AddFileCommand(new OperatorDto("Client", "RUF 1"), "brand.jpg", "image/jpeg", bytes),
            incident, clock, (name, b) => saved.Add((name, b)));

        var file = Assert.Single(incident.Files);
        Assert.Equal("brand.jpg", file.FileName);
        Assert.Equal("Client (RUF 1)", file.AddedBy);
        var write = Assert.Single(saved);
        Assert.Equal($"{file.Id}.jpg", write.StorageFileName);
        Assert.Equal(bytes, write.Bytes);
    }

    [Fact]
    public void AddFileCommand_without_a_byte_writer_still_records_metadata()
    {
        // saveFileBytes is optional so every other command's test (this file) doesn't need to
        // supply one; production (IncidentHost) always does.
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        CommandApplier.Apply(new AddFileCommand(new OperatorDto("Client", null), "x.pdf", "application/pdf", new byte[] { 1 }),
            incident, clock);

        Assert.Single(incident.Files);
    }

    [Fact]
    public void RenameFileCommand_updates_the_display_name()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var file = incident.AddFile(clock, new SessionOperator("Host", "FFB 1"), "brand.jpg", "image/jpeg", 100);

        ApplyOverWire(new RenameFileCommand(file.Id, "Küchenbrand"), incident, clock);

        Assert.Equal("Küchenbrand", Assert.Single(incident.Files).DisplayName);
    }

    [Fact]
    public void A_command_against_a_closed_incident_is_rejected_by_the_domain_guard()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(new CloseIncidentCommand(new OperatorDto("Host", "FFB 1")), incident, clock);

        Assert.Throws<IncidentClosedException>(() =>
            ApplyOverWire(new AddJournalEntryCommand(new OperatorDto("Client", null),
                EtbDirection.Internal, "zu spät", null, null), incident, clock));
    }

    [Fact]
    public void EditJournalEntryCommand_edits_the_entry_and_attributes_the_editor()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        ApplyOverWire(new AddJournalEntryCommand(new OperatorDto("Client", "RUF 1"),
            EtbDirection.Incoming, "Lagemeldung", null, null), incident, clock);
        var entry = incident.Journal.Last();

        ApplyOverWire(new EditJournalEntryCommand(new OperatorDto("Editor", "RUF 2"), entry.Id, "Korrigiert"),
            incident, clock);

        var edited = incident.Journal.Single(e => e.Id == entry.Id);
        Assert.Equal("Korrigiert", edited.Text);
        Assert.Equal("Editor (RUF 2)", Assert.Single(edited.Edits).EditedBy);
    }

    [Fact]
    public void EditJournalEntryCommand_on_a_System_entry_throws()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var systemEntry = incident.Journal.Single(e => e.Direction == EtbDirection.System);

        Assert.Throws<InvalidOperationException>(() =>
            ApplyOverWire(new EditJournalEntryCommand(new OperatorDto("Client", null), systemEntry.Id, "Manipuliert"),
                incident, clock));
    }

    [Fact]
    public void AddTask_is_due_relative_to_the_host_clock_and_attributed_to_the_sender()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(new AddTaskCommand(new OperatorDto("Client", "RUF 1"), "Tür sichern", "FFB 1/44/1",
            TaskImportance.High, TaskUrgency.Medium, 10), incident, clock);

        var task = Assert.Single(incident.Tasks);
        Assert.Equal("Tür sichern", task.Text);
        Assert.Equal(clock.Now.AddMinutes(10), task.DueAt);      // host clock is authoritative
        Assert.Equal("Client (RUF 1)", task.CreatedBy);          // sender attribution, not the host's
    }

    [Fact]
    public void SetTaskCompleted_toggles_completion_with_host_time()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new OperatorDto("Client", null);
        ApplyOverWire(new AddTaskCommand(op, "X", "", TaskImportance.Low, TaskUrgency.Low, 5), incident, clock);
        var taskId = incident.Tasks[0].Id;

        clock.Now = clock.Now.AddMinutes(1);
        ApplyOverWire(new SetTaskCompletedCommand(op, taskId, true), incident, clock);

        Assert.True(incident.Tasks[0].IsCompleted);
        Assert.Equal("Client", incident.Tasks[0].CompletedBy);
    }

    [Fact]
    public void Apply_AddCoBuilding_CreatesBuildingAndDwellings()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var cmd = new AddCoBuildingCommand(new OperatorDto("Test", null), "Haus A", 2, 3);

        ApplyOverWire(cmd, incident, clock);

        Assert.Single(incident.Buildings);
        Assert.Equal(9, incident.Dwellings.Count);
    }

    [Fact]
    public void Apply_RecordCoValue_SetsValue()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        incident.AddCoBuilding(clock, new SessionOperator("Test", null), "Haus A", 2, 3);
        var buildingId = incident.Buildings[0].Id;

        var cmd = new RecordCoValueCommand(new OperatorDto("Test", null), buildingId, 0, 1, 45);
        ApplyOverWire(cmd, incident, clock);

        var dwelling = incident.Dwellings.First(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(45, dwelling.CoValue);
    }

    [Fact]
    public void Apply_SetDwellingStatus_SetsStatus()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        incident.AddCoBuilding(clock, new SessionOperator("Test", null), "Haus A", 2, 3);
        var buildingId = incident.Buildings[0].Id;

        var cmd = new SetDwellingStatusCommand(new OperatorDto("Test", null), buildingId, 0, 1, DwellingStatus.Searched);
        ApplyOverWire(cmd, incident, clock);

        var dwelling = incident.Dwellings.First(d =>
            d.BuildingId == buildingId && d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(DwellingStatus.Searched, dwelling.Status);
    }
}
