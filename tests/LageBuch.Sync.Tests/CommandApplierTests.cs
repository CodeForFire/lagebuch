using LageBuch.Domain;
using LageBuch.Domain.Atemschutz;
using LageBuch.Domain.CoMeasurement;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Leadership;
using LageBuch.Domain.Tasks;

namespace LageBuch.Sync.Tests;

public class CommandApplierTests
{
    private static Incident NewIncident(FixedClock clock) =>
        Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null);

    private static void ApplyOverWire(SyncCommand command, Incident incident, FixedClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);

    [Fact]
    public void SetScbaSafetyTrupp_assigns_and_clears_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var angriff = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);
        var sicherheit = incident.AddScbaTrupp(clock, "Sicherheitstrupp", TruppMember.Crew("Huber", "Maier"), entryPressure: 290);

        ApplyOverWire(new SetScbaSafetyTruppCommand(angriff.Id, sicherheit.Id), incident, clock);
        Assert.Equal(sicherheit.Id, angriff.SafetyTruppId);

        ApplyOverWire(new SetScbaSafetyTruppCommand(angriff.Id, null), incident, clock);
        Assert.Null(angriff.SafetyTruppId);
    }

    [Fact]
    public void RecordScbaPressure_keeps_the_clients_Ort_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);

        ApplyOverWire(new RecordScbaPressureCommand(trupp.Id, 250, "2. OG"), incident, clock);

        Assert.Equal(new PressureReading(clock.Now, 250, "2. OG"), Assert.Single(trupp.PressureReadings));
    }

    [Fact]
    public void RecordScbaPressure_refuses_an_oversized_Ort_from_a_peer()
    {
        // The host does not trust the client's form to have enforced the cap.
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);
        incident.StartScbaTrupp(clock, trupp.Id);

        Assert.Throws<ArgumentException>(() => ApplyOverWire(
            new RecordScbaPressureCommand(trupp.Id, 250, new string('x', AtemschutzTrupp.MaxLocationLength + 1)),
            incident,
            clock));
        Assert.Empty(trupp.PressureReadings);
    }

    [Fact]
    public void SetScbaSafetyTrupp_refuses_an_id_the_host_does_not_know()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var angriff = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"), entryPressure: 300);

        // The host is authoritative: a client command naming a Trupp it does not have is a bug, and
        // storing the dangling id would quietly corrupt the file rather than surface the problem.
        Assert.Throws<KeyNotFoundException>(
            () => ApplyOverWire(new SetScbaSafetyTruppCommand(angriff.Id, Guid.NewGuid()), incident, clock));
    }

    [Fact]
    public void EditRoleAssignment_over_the_wire_corrects_the_holder_in_place()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var assigned = incident.AssignRole(clock, new SessionOperator("Host", "FFB 1"), "S2 Lage", "Meier", section: "Nord");

        ApplyOverWire(
            new EditRoleAssignmentCommand(new OperatorDto("Client", "RUF 1"), assigned.Id, "Maier", "Florian 1/11", "0171", "Süd"),
            incident,
            clock);

        var corrected = Assert.Single(incident.Roles);
        Assert.Equal(assigned.Id, corrected.Id);
        Assert.Equal(("Maier", "Florian 1/11", "0171", "Süd"), (corrected.PersonName, corrected.CallSign, corrected.Phone, corrected.Section));
        Assert.Equal("Client (RUF 1)", incident.Journal[^1].EnteredBy);
    }

    [Fact]
    public void SetAddress_over_the_wire_sets_hausnummer_plz_and_ort_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(new SetAddressCommand("Hauptstraße", "Puch", "1", "82256", "Fürstenfeldbruck"), incident, clock);

        Assert.Equal("Hauptstraße 1, Puch, 82256 Fürstenfeldbruck", Formatting.Address(incident));
    }

    [Fact]
    public void SetInvolvedPartyDwelling_over_the_wire_links_with_the_hosts_own_label()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Host", "FFB 1");
        incident.AddCoBuilding(clock, op, "Hauptstr. 12", floorCount: 1, apartmentsPerFloor: 2);
        var dwelling = incident.Dwellings.Single(d => d.FloorOrdinal == 1 && d.ApartmentNumber == 1);
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);

        ApplyOverWire(new SetInvolvedPartyDwellingCommand(party.Id, dwelling.Id), incident, clock);
        Assert.Equal("Hauptstr. 12, 1. OG, Whg. 1", incident.InvolvedParties.Single().DwellingLabel);

        ApplyOverWire(new SetInvolvedPartyDwellingCommand(party.Id, null), incident, clock);
        Assert.Null(incident.InvolvedParties.Single().DwellingId);
    }

    [Fact]
    public void SetInvolvedPartyWhereabouts_over_the_wire_is_stamped_with_the_hosts_clock()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Host", "FFB 1");
        var party = incident.AddInvolvedParty(clock, op, "Erika Beispiel", null, null);

        ApplyOverWire(new SetInvolvedPartyWhereaboutsCommand(party.Id, "bei Nachbarin Frau Meier"), incident, clock);
        var placed = incident.InvolvedParties.Single();
        Assert.Equal("bei Nachbarin Frau Meier", placed.Whereabouts);
        Assert.Equal(clock.Now, placed.WhereaboutsAt);

        ApplyOverWire(new SetInvolvedPartyWhereaboutsCommand(party.Id, null), incident, clock);
        Assert.Null(incident.InvolvedParties.Single().Whereabouts);
    }

    [Fact]
    public void Applying_a_non_file_command_returns_null()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new OperatorDto("Client", "RUF 1");

        var result = CommandApplier.Apply(new AddForceUnitCommand(op, "Aich", 9, "Aich 42/1", null, null, 4, 1), incident, clock);

        Assert.Null(result);
    }

    [Fact]
    public void Journal_entry_is_attributed_to_the_commands_operator_not_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(
            new AddJournalEntryCommand(
                new OperatorDto("Client", "RUF 1"),
                EtbDirection.Incoming,
                "Meldung von der Einsatzstelle",
                "Leitstelle",
                "ELW"),
            incident,
            clock);

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
        incident.SeedChecklist(new[]
        {
            new ChecklistSeed(
                ChecklistDefaults.AufbauListId,
                ChecklistDefaults.AufbauTitle,
                new[] { ("Punkt A", false), ("Punkt B", false) }),
        });
        var op = new OperatorDto("Client", "RUF 1");

        ApplyOverWire(new ToggleChecklistItemCommand(op, incident.Checklists[0].Items[0].Id), incident, clock);
        ApplyOverWire(new AssignRoleCommand(op, "EL", "Huber", "FFB 1", clock.Now, null, null, null), incident, clock);
        ApplyOverWire(new AddForceUnitCommand(op, "Aich", 9, "Aich 42/1", "Im Einsatz", null, 4, 1), incident, clock);
        ApplyOverWire(new UpdateForceStrengthCommand(op, incident.Forces[0].Id, 2, 12, 6), incident, clock);
        ApplyOverWire(
            new AddScbaTruppCommand(
                "Angriffstrupp",
                new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
                "AT-1",
                null,
                30,
                60,
                5,
                EntryPressure: 300),
            incident,
            clock);
        ApplyOverWire(new StartScbaTruppCommand(incident.ScbaTrupps.Last().Id), incident, clock);

        Assert.True(incident.Checklists[0].Items[0].IsDone);
        Assert.Single(incident.Roles);
        Assert.Equal(12, incident.TotalPersonnel);

        // The strength correction arrived over the wire with officer count and edit trail intact.
        var corrected = incident.Forces[0];
        var actual = (
            corrected.OfficerCount,
            corrected.PersonnelCount,
            corrected.ScbaCount,
            incident.Journal.Count(e => e.Text.Contains('→', StringComparison.Ordinal)));
        Assert.Equal((2, 12, 6, 1), actual);
        var trupp = Assert.Single(incident.ScbaTrupps);
        Assert.Equal(300, trupp.EntryPressure);
    }

    [Fact]
    public void AddFileCommand_records_metadata_under_the_clients_own_id_and_returns_the_new_file()
    {
        // issue #167 P1 #2: Apply never sees attachment bytes at all — they PUT to a separate
        // endpoint, keyed by the id the client already generated for this AddFileCommand. Apply's
        // only job here is the domain mutation, recorded under that same id.
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var fileId = Guid.NewGuid();

        var file = CommandApplier.Apply(
            new AddFileCommand(new OperatorDto("Client", "RUF 1"), fileId, "brand.jpg", "image/jpeg", 3),
            incident,
            clock);

        var recorded = Assert.Single(incident.Files);
        Assert.Same(recorded, file);
        Assert.Equal(fileId, file!.Id);
        Assert.Equal("brand.jpg", file.FileName);
        Assert.Equal("Client (RUF 1)", file.AddedBy);
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
    public void RemoveFileCommand_removes_the_file_and_returns_it_with_the_commands_operator_attributed()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var addOp = new OperatorDto("Client", "RUF 1");
        var file = CommandApplier.Apply(
            new AddFileCommand(addOp, Guid.NewGuid(), "brand.jpg", "image/jpeg", 3), incident, clock)!;
        var before = incident.Journal.Count;

        var removeOp = new OperatorDto("Other", "RUF 2");
        var removed = CommandApplier.Apply(new RemoveFileCommand(removeOp, file.Id), incident, clock);

        Assert.Empty(incident.Files);
        Assert.Equal(file.Id, removed!.Id);
        var entry = incident.Journal.Last();
        Assert.Equal(before + 1, incident.Journal.Count);
        Assert.Equal("Datei entfernt: brand.jpg", entry.Text);
        Assert.Equal("Other (RUF 2)", entry.EnteredBy);
    }

    [Fact]
    public void A_command_against_a_closed_incident_is_rejected_by_the_domain_guard()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(new CloseIncidentCommand(new OperatorDto("Host", "FFB 1")), incident, clock);

        Assert.Throws<IncidentClosedException>(() =>
            ApplyOverWire(
                new AddJournalEntryCommand(
                    new OperatorDto("Client", null),
                    EtbDirection.Internal,
                    "zu spät",
                    null,
                    null),
                incident,
                clock));
    }

    [Fact]
    public void EditJournalEntryCommand_edits_the_entry_and_attributes_the_editor()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        ApplyOverWire(
            new AddJournalEntryCommand(
                new OperatorDto("Client", "RUF 1"),
                EtbDirection.Incoming,
                "Lagemeldung",
                null,
                null),
            incident,
            clock);
        var entry = incident.Journal.Last();

        ApplyOverWire(
            new EditJournalEntryCommand(new OperatorDto("Editor", "RUF 2"), entry.Id, "Korrigiert"),
            incident,
            clock);

        var edited = incident.Journal.Single(e => e.Id == entry.Id);
        Assert.Equal("Korrigiert", edited.Text);
        Assert.Equal("Editor (RUF 2)", Assert.Single(edited.Edits).EditedBy);
    }

    [Fact]
    public void ChangeOperatorCommand_logs_the_handover_credited_to_the_new_operator()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(
            new ChangeOperatorCommand(new OperatorDto("Client", "RUF 1"), new OperatorDto("Schmidt", "RUF 2")),
            incident,
            clock);

        var entry = incident.Journal.Last();
        Assert.Equal("Lagebuchführerwechsel: Client (RUF 1) → Schmidt (RUF 2)", entry.Text);
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal("Schmidt (RUF 2)", entry.EnteredBy);
        Assert.Contains(incident.Audit, a => a.Action == "operator-changed");
    }

    [Fact]
    public void EditJournalEntryCommand_on_a_System_entry_throws()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var systemEntry = incident.Journal.Single(e => e.Direction == EtbDirection.System);

        Assert.Throws<InvalidOperationException>(() =>
            ApplyOverWire(
                new EditJournalEntryCommand(new OperatorDto("Client", null), systemEntry.Id, "Manipuliert"),
                incident,
                clock));
    }

    [Fact]
    public void AddTask_is_due_relative_to_the_host_clock_and_attributed_to_the_sender()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(
            new AddTaskCommand(
                new OperatorDto("Client", "RUF 1"),
                "Tür sichern",
                "FFB 1/44/1",
                TaskImportance.High,
                TaskUrgency.Medium,
                10),
            incident,
            clock);

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
        ApplyOverWire(new AddTaskCommand(op, "X", string.Empty, TaskImportance.Low, TaskUrgency.Low, 5), incident, clock);
        var taskId = incident.Tasks[0].Id;

        clock.Now = clock.Now.AddMinutes(1);
        ApplyOverWire(new SetTaskCompletedCommand(op, taskId, true), incident, clock);

        Assert.True(incident.Tasks[0].IsCompleted);
        Assert.Equal("Client", incident.Tasks[0].CompletedBy);
    }

    [Fact]
    public void UpdateTask_corrects_the_editable_fields()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new OperatorDto("Client", null);
        ApplyOverWire(new AddTaskCommand(op, "Tür sichren", string.Empty, TaskImportance.Low, TaskUrgency.Low, 5), incident, clock);
        var taskId = incident.Tasks[0].Id;

        ApplyOverWire(
            new UpdateTaskCommand(taskId, "Tür sichern", "FFB 1/44/1", TaskImportance.High, TaskUrgency.Medium),
            incident,
            clock);

        var task = incident.Tasks[0];
        Assert.Equal("Tür sichern", task.Text);
        Assert.Equal("FFB 1/44/1", task.Assignee);
        Assert.Equal(TaskImportance.High, task.Importance);
        Assert.Equal(TaskUrgency.Medium, task.Urgency);
    }

    [Fact]
    public void ExtendTaskTimer_adds_minutes_to_the_due_time()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new OperatorDto("Client", null);
        ApplyOverWire(new AddTaskCommand(op, "X", string.Empty, TaskImportance.Low, TaskUrgency.Low, 5), incident, clock);
        var taskId = incident.Tasks[0].Id;
        var before = incident.Tasks[0].DueAt;

        ApplyOverWire(new ExtendTaskTimerCommand(taskId, 5), incident, clock);

        Assert.Equal(before.AddMinutes(5), incident.Tasks[0].DueAt);
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

    // #420: a Haus picked from an Objekt arrives with its Wohnungsbezeichnungen in the same command,
    // because a joined client never learns the new Haus's id to label it afterwards.
    [Fact]
    public void Apply_AddCoBuilding_creates_the_haus_with_the_labels_it_carries()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var labels = new Dictionary<string, string?> { ["0:1"] = "Hausmeister", ["-1:2"] = "Heizungskeller" };

        ApplyOverWire(new AddCoBuildingCommand(new OperatorDto("Test", null), "Haus A", 2, 3, 1, labels), incident, clock);

        var building = Assert.Single(incident.Buildings);
        Assert.Equal("Hausmeister", building.ApartmentLabels["0:1"]);
        Assert.Equal("Heizungskeller", building.ApartmentLabels["-1:2"]);
    }

    // The labels come from a peer on a shared LAN: one naming Wohnungen the Haus does not have, or
    // carrying a megabyte of text, must not reach the incident file.
    [Fact]
    public void Apply_AddCoBuilding_drops_hostile_labels_from_a_peer()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var labels = new Dictionary<string, string?> { ["0:1"] = new string('x', 10_000) };
        for (var i = 0; i < 1_000; i++)
        {
            labels[$"{i + 10}:1"] = "kein solches Geschoss";
        }

        ApplyOverWire(new AddCoBuildingCommand(new OperatorDto("Test", null), "Haus A", 2, 3, 0, labels), incident, clock);

        var building = Assert.Single(incident.Buildings);
        Assert.Equal("0:1", Assert.Single(building.ApartmentLabels).Key);
        Assert.Equal(Building.MaxApartmentLabelLength, building.ApartmentLabels["0:1"]!.Length);
    }

    // #419: the removal has to survive the wire as a selection, not a count -- a joined device
    // must delete the same Wohnungen the host's crew ticked, not the ones on the right-hand end.
    [Fact]
    public void Apply_RemoveDwellings_RemovesThePickedUnitsAndRenumbersTheRest()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Test", null);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 4);
        var buildingId = incident.Buildings[0].Id;
        incident.SetDwellingDetails(buildingId, 0, 3, "Müller", KeyAvailability.Unknown);

        var cmd = new RemoveDwellingsCommand(new OperatorDto("Test", null), buildingId, 0, new[] { 1, 2 });
        ApplyOverWire(cmd, incident, clock);

        var floor = incident.Dwellings
            .Where(d => d.FloorOrdinal == 0)
            .OrderBy(d => d.ApartmentNumber)
            .ToList();
        Assert.Equal(new[] { 1, 2 }, floor.Select(d => d.ApartmentNumber));
        Assert.Equal("Müller", floor[0].ResidentName);
        Assert.Equal(2, incident.Buildings[0].ApartmentsFor(0));
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

    // #424: the command carries only the new value. The reading's time comes from the HOST clock
    // and its attribution from the command's operator, so a client cannot forge a measurement
    // time -- which is exactly why RecordCoValueCommand did not need a timestamp field.
    [Fact]
    public void Apply_RecordCoValue_MintsAReadingWithTheHostClockAndTheCommandsOperator()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        ApplyOverWire(new AddCoBuildingCommand(new OperatorDto("Test", null), "Haus A", 2, 3), incident, clock);
        var buildingId = incident.Buildings[0].Id;

        ApplyOverWire(new RecordCoValueCommand(new OperatorDto("Huber", "FFB 12/1"), buildingId, 0, 1, 120), incident, clock);
        clock.Now = clock.Now.AddMinutes(27);
        ApplyOverWire(new RecordCoValueCommand(new OperatorDto("Huber", "FFB 12/1"), buildingId, 0, 1, 40), incident, clock);

        var dwelling = incident.Dwellings.First(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1);
        Assert.Equal(new int?[] { 120, 40 }, dwelling.Readings.Select(r => r.Value));
        Assert.All(dwelling.Readings, r => Assert.Equal("Huber (FFB 12/1)", r.RecordedBy));
        Assert.Equal(27, (dwelling.Readings[1].MeasuredAt - dwelling.Readings[0].MeasuredAt).TotalMinutes);
    }

    // A resent command must not look like a second measurement.
    [Fact]
    public void Apply_RecordCoValue_Twice_WithTheSameValue_AppendsOneReading()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        ApplyOverWire(new AddCoBuildingCommand(new OperatorDto("Test", null), "Haus A", 2, 3), incident, clock);
        var buildingId = incident.Buildings[0].Id;
        var cmd = new RecordCoValueCommand(new OperatorDto("Huber", null), buildingId, 0, 1, 45);

        ApplyOverWire(cmd, incident, clock);
        ApplyOverWire(cmd, incident, clock);

        Assert.Single(incident.Dwellings.First(d => d.FloorOrdinal == 0 && d.ApartmentNumber == 1).Readings);
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

    [Fact]
    public void Involved_party_commands_add_update_and_remove_on_the_host_without_an_etb_line()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var journalBefore = incident.Journal.Count;

        ApplyOverWire(new AddInvolvedPartyCommand(new OperatorDto("Client", "FFB 2"), "Erika Beispiel", null, null), incident, clock);
        var party = Assert.Single(incident.InvolvedParties);
        Assert.Equal("Client (FFB 2)", party.CreatedBy);
        Assert.Equal(clock.Now, party.CreatedAt);

        ApplyOverWire(new UpdateInvolvedPartyCommand(party.Id, "Erika Beispiel", "0171 0000001", "Hauseigentümerin"), incident, clock);
        Assert.Equal("0171 0000001", Assert.Single(incident.InvolvedParties).Phone);

        ApplyOverWire(new RemoveInvolvedPartyCommand(party.Id), incident, clock);
        Assert.Empty(incident.InvolvedParties);

        Assert.Equal(journalBefore, incident.Journal.Count);
    }

    [Fact]
    public void An_oversized_involved_party_name_from_a_peer_is_refused_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var oversized = new string('A', Domain.Involved.InvolvedParty.MaxNameLength + 1);

        Assert.Throws<ArgumentException>(() =>
            ApplyOverWire(new AddInvolvedPartyCommand(new OperatorDto("Client", null), oversized, null, null), incident, clock));

        Assert.Empty(incident.InvolvedParties);
    }

    [Fact]
    public void An_oversized_involved_party_update_from_a_peer_leaves_the_entry_untouched()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var party = incident.AddInvolvedParty(clock, new SessionOperator("Host"), "Erika Beispiel", null, null);
        var oversizedNotes = new string('x', Domain.Involved.InvolvedParty.MaxNotesLength + 1);

        Assert.Throws<ArgumentException>(() =>
            ApplyOverWire(new UpdateInvolvedPartyCommand(party.Id, "Erika Beispiel", null, oversizedNotes), incident, clock));

        Assert.Same(party, Assert.Single(incident.InvolvedParties));
    }

    [Fact]
    public void Removing_an_involved_party_the_host_does_not_know_throws()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        Assert.Throws<KeyNotFoundException>(() => ApplyOverWire(new RemoveInvolvedPartyCommand(Guid.NewGuid()), incident, clock));
    }

    [Theory]
    [InlineData(true, KeyAvailability.Available)]
    [InlineData(false, KeyAvailability.NotAvailable)]
    [InlineData(null, KeyAvailability.Unknown)]
    public void Apply_UpdateDwellingDetails_maps_the_wire_bool_onto_the_key_availability(
        bool? onTheWire, KeyAvailability expected)
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Test", null);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 1);
        var buildingId = incident.Buildings[0].Id;
        incident.SetDwellingDetails(buildingId, 0, 1, null, KeyAvailability.Available);

        ApplyOverWire(new UpdateDwellingDetailsCommand(buildingId, 0, 1, "Müller", onTheWire), incident, clock);

        Assert.Equal(expected, incident.Dwellings[0].KeyAvailable);
    }

    [Fact]
    public void Apply_UpdateDwellingDetails_records_the_Schluesselnummer()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Test", null);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 1);
        var buildingId = incident.Buildings[0].Id;

        ApplyOverWire(new UpdateDwellingDetailsCommand(buildingId, 0, 1, null, true, "B-14"), incident, clock);

        Assert.Equal("B-14", incident.Dwellings[0].KeyNumber);
    }

    // A peer is hostile input: a Schlüsselnummer of any length must not reach the incident file.
    [Fact]
    public void Apply_UpdateDwellingDetails_rejects_an_oversized_Schluesselnummer()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Test", null);
        incident.AddCoBuilding(clock, op, "Haus A", 1, 1);
        var buildingId = incident.Buildings[0].Id;
        var oversized = new string('9', 100_000);

        Assert.Throws<ArgumentException>(() =>
            ApplyOverWire(new UpdateDwellingDetailsCommand(buildingId, 0, 1, null, true, oversized), incident, clock));
        Assert.Null(incident.Dwellings[0].KeyNumber);
    }

    [Fact]
    public void A_client_minted_entry_id_lets_its_aufgabe_link_before_the_snapshot_returns()
    {
        // The joined device's HINZUFÜGEN & AUFGABE: it mints the entry's id, sends the entry, then
        // the Aufgabe naming that id -- without ever having seen the host's journal.
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var minted = Guid.NewGuid();
        var op = new OperatorDto("Client", "RUF 1");

        ApplyOverWire(new AddJournalEntryCommand(op, EtbDirection.Incoming, "Stromleitung hängt tief", "ILS", null, minted), incident, clock);
        ApplyOverWire(new AddTaskCommand(op, "EVU informieren", string.Empty, TaskImportance.High, TaskUrgency.High, 5, minted), incident, clock);

        Assert.Contains(incident.Journal, e => e.Id == minted);
        Assert.Equal(minted, Assert.Single(incident.Tasks).SourceEntryId);
    }

    [Fact]
    public void The_link_survives_the_task_overtaking_its_entry_on_the_way_to_the_host()
    {
        // A client older than its per-device command order sent the two as racing POSTs, so either
        // may land first, and a host still has to cope with such a peer.
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var minted = Guid.NewGuid();
        var op = new OperatorDto("Client", "RUF 1");

        ApplyOverWire(new AddTaskCommand(op, "EVU informieren", string.Empty, TaskImportance.High, TaskUrgency.High, 5, minted), incident, clock);
        ApplyOverWire(new AddJournalEntryCommand(op, EtbDirection.Incoming, "Stromleitung hängt tief", "ILS", null, minted), incident, clock);

        Assert.Equal(minted, Assert.Single(incident.Tasks).SourceEntryId);
        Assert.Contains(incident.Journal, e => e.Id == minted);
    }

    [Fact]
    public void A_client_offering_an_entry_id_already_in_the_journal_gets_a_new_one()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var taken = incident.Journal[0].Id;

        ApplyOverWire(new AddJournalEntryCommand(new OperatorDto("Client", null), EtbDirection.Incoming, "Doppelt", null, null, taken), incident, clock);

        Assert.Equal(incident.Journal.Count, incident.Journal.Select(e => e.Id).Distinct().Count());
        Assert.Equal("Doppelt", incident.Journal[^1].Text);
    }

    [Fact]
    public void Issued_item_commands_issue_correct_return_and_remove_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var journalBefore = incident.Journal.Count;

        ApplyOverWire(new IssueItemCommand(new OperatorDto("Client", "ELW 1"), "Mobilteil 1", "Erika Beispiel", "0171 0000001", null, null), incident, clock);
        var issued = Assert.Single(incident.IssuedItems);
        Assert.Equal("Client (ELW 1)", issued.IssuedBy);
        Assert.Equal(clock.Now, issued.IssuedAt);

        ApplyOverWire(new UpdateIssuedItemCommand(issued.Id, "Mobilteil 1", "Erika Beispiel", "0171 0000001", "THW OV Beispielstadt", null), incident, clock);
        Assert.Equal("THW OV Beispielstadt", Assert.Single(incident.IssuedItems).IssuedToDetails);

        ApplyOverWire(new ReturnIssuedItemCommand(new OperatorDto("Client", "ELW 1"), issued.Id), incident, clock);
        Assert.Equal("Client (ELW 1)", Assert.Single(incident.IssuedItems).ReturnedBy);

        ApplyOverWire(new UndoIssuedItemReturnCommand(issued.Id), incident, clock);
        Assert.True(Assert.Single(incident.IssuedItems).IsOut);

        ApplyOverWire(new RemoveIssuedItemCommand(issued.Id), incident, clock);
        Assert.Empty(incident.IssuedItems);

        // The recipient's name and phone number never reach the ETB.
        Assert.Equal(journalBefore, incident.Journal.Count);
    }

    [Fact]
    public void An_oversized_issued_item_field_from_a_peer_is_refused_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var oversized = new string('1', Domain.Equipment.IssuedItem.MaxPhoneLength + 1);

        Assert.Throws<ArgumentException>(() =>
            ApplyOverWire(new IssueItemCommand(new OperatorDto("Client", null), "AED", "Max Beispiel", oversized, null, null), incident, clock));

        Assert.Empty(incident.IssuedItems);
    }

    [Fact]
    public void Situation_report_commands_draft_correct_and_send_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var journalBefore = incident.Journal.Count;

        ApplyOverWire(new AddSituationReportCommand(new OperatorDto("Client", "ELW 1"), "ELW 1", "Blumenstraße 13", null, null, null, null), incident, clock);
        var draft = Assert.Single(incident.SituationReports);
        Assert.Equal("Client (ELW 1)", draft.CreatedBy);

        ApplyOverWire(new UpdateSituationReportCommand(draft.Id, "ELW 1", "Blumenstraße 13", "Schuppen im Vollbrand", null, null, null), incident, clock);
        Assert.Equal(journalBefore, incident.Journal.Count);

        ApplyOverWire(new SendSituationReportCommand(new OperatorDto("Client", "ELW 1"), draft.Id, "ILS"), incident, clock);

        var sent = Assert.Single(incident.SituationReports);
        Assert.Equal(1, sent.Number);
        var entry = incident.Journal[^1];
        Assert.Equal(Domain.Etb.EtbDirection.Outgoing, entry.Direction);
        Assert.Equal("ELW 1", entry.From);
        Assert.Equal("ILS", entry.To);
        Assert.Equal("Lagemeldung Nr. 1: Blumenstraße 13 · Schuppen im Vollbrand", entry.Text);
    }

    [Fact]
    public void An_oversized_situation_report_field_from_a_peer_is_refused_on_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var oversized = new string('x', Domain.SituationReports.SituationReport.MaxFieldLength + 1);

        Assert.Throws<ArgumentException>(() =>
            ApplyOverWire(new AddSituationReportCommand(new OperatorDto("Client", null), null, null, oversized, null, null, null), incident, clock));

        Assert.Empty(incident.SituationReports);
    }

    [Fact]
    public void An_oversized_leitstelle_name_from_a_peer_leaves_the_draft_unsent()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var draft = incident.AddSituationReport(clock, new SessionOperator("Host"), null, null, "Lage", null, null, null);
        var journalBefore = incident.Journal.Count;

        Assert.Throws<ArgumentException>(() =>
            ApplyOverWire(new SendSituationReportCommand(new OperatorDto("Client", null), draft.Id, new string('I', 5000)), incident, clock));

        Assert.False(Assert.Single(incident.SituationReports).IsSent);
        Assert.Equal(journalBefore, incident.Journal.Count);
    }

    [Fact]
    public void A_peer_cannot_rewrite_a_sent_situation_report()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var op = new SessionOperator("Host");
        var sent = incident.SendSituationReport(
            clock, op, incident.AddSituationReport(clock, op, null, null, "Lage", null, null, null).Id, "ILS");

        Assert.Throws<InvalidOperationException>(() =>
            ApplyOverWire(new UpdateSituationReportCommand(sent.Id, null, null, "anders", null, null, null), incident, clock));
        Assert.Throws<InvalidOperationException>(() =>
            ApplyOverWire(new RemoveSituationReportCommand(sent.Id), incident, clock));

        Assert.Equal("Lage", Assert.Single(incident.SituationReports).Situation);
    }

    [Fact]
    public void Add_command_node_with_details_over_the_wire_creates_the_node_with_them_for_the_sender()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var adder = new OperatorDto("Maier", "FFB 2");
        var entries = incident.Journal.Count;

        ApplyOverWire(
            new AddCommandNodeCommand(adder, CommandNodeKind.Abschnitt, "Nord", null, "Brandbekämpfung", "Hauptstraße", "TG 2", "Florian Nord", "0171"),
            incident,
            clock);

        var node = Assert.Single(incident.CommandNodes);
        Assert.Equal("Brandbekämpfung", node.Auftrag);
        Assert.Equal("Hauptstraße", node.Standort);
        Assert.Equal("TG 2", node.Funkgruppe);
        Assert.Equal("Florian Nord", node.Rufname);
        Assert.Equal("0171", node.Telefon);
        Assert.Equal(entries + 3, incident.Journal.Count);
        Assert.All(incident.Journal.Skip(entries), e => Assert.Equal("Maier (FFB 2)", e.EnteredBy));
    }

    [Fact]
    public void Abschnitt_commands_over_the_wire_form_update_move_and_dissolve_attributed_to_the_sender()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var host = new SessionOperator("Host", "FFB 1");
        var unit = incident.AddForceUnit(clock, host, "Aich", 9, "Aich 42/1", null, null, 4);
        var adder = new OperatorDto("Maier", "FFB 2");
        var editor = new OperatorDto("Bauer", "FFB 3");
        var mover = new OperatorDto("Schuster", "FFB 4");
        var dissolver = new OperatorDto("Lang", "FFB 5");

        ApplyOverWire(new AddCommandNodeCommand(adder, CommandNodeKind.Abschnitt, "Nord", null), incident, clock);
        var node = Assert.Single(incident.CommandNodes);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);

        var entries = incident.Journal.Count;
        ApplyOverWire(new UpdateCommandNodeCommand(editor, node.Id, "Nordseite", "Brandbekämpfung", "Hauptstraße", "TG 2", "Florian Nord", "0171"), incident, clock);
        var updated = incident.CommandNodes.Single();
        Assert.Equal("Nordseite", updated.Name);
        Assert.Equal("Brandbekämpfung", updated.Auftrag);
        Assert.Equal("TG 2", updated.Funkgruppe);

        // Rename, Auftrag and Funkgruppe each write a line; all three belong to the sender.
        Assert.Equal(entries + 3, incident.Journal.Count);
        Assert.All(incident.Journal.Skip(entries), e => Assert.Equal("Bauer (FFB 3)", e.EnteredBy));

        entries = incident.Journal.Count;
        ApplyOverWire(new MoveForceUnitCommand(mover, unit.Id, node.Id), incident, clock);
        Assert.Equal(node.Id, incident.Forces.Single().NodeId);
        Assert.Equal(entries + 1, incident.Journal.Count);
        Assert.Equal("Schuster (FFB 4)", incident.Journal[^1].EnteredBy);

        entries = incident.Journal.Count;
        ApplyOverWire(new MoveForceUnitCommand(dissolver, unit.Id, null), incident, clock);
        Assert.Null(incident.Forces.Single().NodeId);
        Assert.Equal(entries + 1, incident.Journal.Count);
        Assert.Equal("Lang (FFB 5)", incident.Journal[^1].EnteredBy);

        ApplyOverWire(new DissolveCommandNodeCommand(adder, node.Id), incident, clock);
        Assert.False(incident.CommandNodes.Single().IsLive);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);
    }

    [Fact]
    public void Fuehrungsstufe_and_leadership_post_over_the_wire_are_set_and_attributed()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var client = new OperatorDto("Maier", "FFB 2");

        ApplyOverWire(new SetFuehrungsstufeCommand(client, Fuehrungsstufe.C), incident, clock);
        Assert.Equal(Fuehrungsstufe.C, incident.Fuehrungsstufe);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);

        ApplyOverWire(new UpdateLeadershipPostCommand(client, "Feuerwehrhaus", "Florian EL", "0172", "TG 1"), incident, clock);
        Assert.Equal("Feuerwehrhaus", incident.LeadershipPost.Standort);
        Assert.Equal("TG 1", incident.LeadershipPost.Funkgruppe);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);

        ApplyOverWire(new SetFuehrungsstufeCommand(client, null), incident, clock);
        Assert.Null(incident.Fuehrungsstufe);
    }

    private static AddChecklistCommand AddChecklistOf(OperatorDto op, Guid id, int items) =>
        new(op, id, "Lagemeldung", Enumerable.Range(0, items).Select(i => new ChecklistSeedItemDto($"Punkt {i}", i == 0)).ToList());

    [Fact]
    public void Set_nav_visibility_command_applies_to_the_host()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);

        ApplyOverWire(new SetNavVisibilityCommand(new OperatorDto("Maier", "FFB 2"), "lagekarte", false), incident, clock);

        Assert.False(incident.NavOverrides["lagekarte"]);
    }

    [Fact]
    public void Add_checklist_command_applies_to_the_host_and_logs_the_operator()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var id = Guid.NewGuid();

        ApplyOverWire(AddChecklistOf(new OperatorDto("Maier", "FFB 2"), id, 3), incident, clock);

        var list = Assert.Single(incident.Checklists, c => c.Id == id);
        Assert.Equal("Lagemeldung", list.Title);
        Assert.Equal(3, list.Items.Count);
        Assert.True(list.Items[0].IsMandatory);
        Assert.False(list.Items[1].IsMandatory);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);
    }

    [Fact]
    public void An_oversized_add_checklist_command_is_rejected()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var before = incident.Checklists.Count;
        var journal = incident.Journal.Count;

        Assert.Throws<ArgumentException>(
            () => ApplyOverWire(AddChecklistOf(new OperatorDto("Maier", "FFB 2"), Guid.NewGuid(), 201), incident, clock));

        Assert.Equal(before, incident.Checklists.Count);
        Assert.Equal(journal, incident.Journal.Count);
    }

    [Fact]
    public void Adding_a_checklist_already_in_the_einsatz_is_refused()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var id = Guid.NewGuid();
        var op = new OperatorDto("Maier", "FFB 2");
        ApplyOverWire(AddChecklistOf(op, id, 2), incident, clock);

        Assert.Throws<InvalidOperationException>(() => ApplyOverWire(AddChecklistOf(op, id, 2), incident, clock));

        Assert.Single(incident.Checklists, c => c.Id == id);
    }

    [Fact]
    public void AssignRole_over_the_wire_places_the_funktion_in_the_abschnitt()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var node = incident.AddCommandNode(clock, new SessionOperator("Host", "FFB 1"), CommandNodeKind.Abschnitt, "Nord");

        ApplyOverWire(
            new AssignRoleCommand(new OperatorDto("Maier", "FFB 2"), "Abschnittsleiter", "Huber", null, null, null, null, null, node.Id, RoleSlot.Leiter),
            incident,
            clock);

        var role = Assert.Single(incident.Roles);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);
        Assert.Equal(node.Id, role.NodeId);
        Assert.Equal(RoleSlot.Leiter, role.Slot);
        Assert.Equal("Nord", role.Section);
    }

    [Fact]
    public void PlaceRoleAssignment_over_the_wire_moves_the_funktion_attributed_to_the_sender()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var host = new SessionOperator("Host", "FFB 1");
        var node = incident.AddCommandNode(clock, host, CommandNodeKind.Abschnitt, "Nord");
        var role = incident.AssignRole(clock, host, "Abschnittsleiter", "Huber");

        ApplyOverWire(
            new PlaceRoleAssignmentCommand(new OperatorDto("Maier", "FFB 2"), role.Id, node.Id, RoleSlot.Leiter),
            incident,
            clock);

        var placed = Assert.Single(incident.Roles);
        Assert.Equal(node.Id, placed.NodeId);
        Assert.Equal(RoleSlot.Leiter, placed.Slot);
        Assert.Equal("Nord", placed.Section);
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);
    }

    [Theory]
    [InlineData("""{"$type":"addCommandNode","operator":{"name":"M","callSign":null},"kind":99,"name":"Nord","parentId":null}""")]
    [InlineData("""{"$type":"setFuehrungsstufe","operator":{"name":"M","callSign":null},"stufe":7}""")]
    [InlineData("""{"$type":"assignRole","operator":{"name":"M","callSign":null},"role":"AL","personName":"H","callSign":null,"from":null,"to":null,"section":null,"phone":null,"nodeId":null,"slot":42}""")]
    [InlineData("""{"$type":"placeRoleAssignment","operator":{"name":"M","callSign":null},"assignmentId":"00000000-0000-0000-0000-000000000001","nodeId":null,"slot":42}""")]
    public void A_hostile_peer_sending_an_integer_outside_the_enum_is_refused_and_the_incident_is_unchanged(string json)
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var entries = incident.Journal.Count;

        // JsonStringEnumConverter also reads integers, so the value deserializes; the domain must refuse it.
        var command = SyncJson.Deserialize<SyncCommand>(json);
        Assert.Throws<ArgumentOutOfRangeException>(() => CommandApplier.Apply(command, incident, clock));

        Assert.Empty(incident.CommandNodes);
        Assert.Empty(incident.Roles);
        Assert.Null(incident.Fuehrungsstufe);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void MoveCommandNode_over_the_wire_moves_the_node_attributed_to_the_sender()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var host = new SessionOperator("Host", "FFB 1");
        var nord = incident.AddCommandNode(clock, host, CommandNodeKind.Abschnitt, "Nord");
        var sued = incident.AddCommandNode(clock, host, CommandNodeKind.Abschnitt, "Süd");

        ApplyOverWire(new MoveCommandNodeCommand(new OperatorDto("Maier", "FFB 2"), sued.Id, nord.Id, null), incident, clock);

        var moved = incident.FindCommandNode(sued.Id)!;
        Assert.Equal((CommandNodeKind.Unterabschnitt, nord.Id), (moved.Kind, moved.ParentId));
        Assert.Equal("Maier (FFB 2)", incident.Journal[^1].EnteredBy);

        ApplyOverWire(new MoveCommandNodeCommand(new OperatorDto("Maier", "FFB 2"), sued.Id, null, nord.Id), incident, clock);
        Assert.Equal([sued.Id, nord.Id], incident.CommandNodes.Select(n => n.Id));
    }

    // A peer may send any ids: one this incident does not hold, a node into itself, or a parent
    // that is a partner. Each is refused and leaves the tree as it was.
    [Theory]
    [InlineData("unknown")]
    [InlineData("itself")]
    [InlineData("into-partner")]
    [InlineData("before-unknown")]
    public void A_hostile_move_is_refused_and_the_incident_is_unchanged(string scenario)
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        var host = new SessionOperator("Host", "FFB 1");
        var nord = incident.AddCommandNode(clock, host, CommandNodeKind.Abschnitt, "Nord");
        var rd = incident.AddCommandNode(clock, host, CommandNodeKind.Partner, "Rettungsdienst");
        var before = incident.CommandNodes.ToList();
        var entries = incident.Journal.Count;
        var peer = new OperatorDto("Maier", "FFB 2");
        SyncCommand command = scenario switch
        {
            "unknown" => new MoveCommandNodeCommand(peer, Guid.NewGuid(), null, null),
            "itself" => new MoveCommandNodeCommand(peer, nord.Id, nord.Id, null),
            "into-partner" => new MoveCommandNodeCommand(peer, nord.Id, rd.Id, null),
            _ => new MoveCommandNodeCommand(peer, nord.Id, null, Guid.NewGuid()),
        };

        Assert.ThrowsAny<Exception>(() => ApplyOverWire(command, incident, clock));

        Assert.Equal(before, incident.CommandNodes);
        Assert.Equal(entries, incident.Journal.Count);
    }
}
