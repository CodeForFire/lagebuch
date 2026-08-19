using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;

namespace Feuerwehr.Sync.Tests;

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
    public void Applying_a_sequence_of_commands_converges_the_incident()
    {
        var clock = new FixedClock();
        var incident = NewIncident(clock);
        incident.SeedChecklist(new[] { ("Punkt A", false), ("Punkt B", false) }, Array.Empty<(string, bool)>());
        var op = new OperatorDto("Client", "RUF 1");

        ApplyOverWire(new ToggleChecklistItemCommand(op, incident.ChecklistAufbau[0].Id), incident, clock);
        ApplyOverWire(new AssignRoleCommand("EL", "Huber", "FFB 1", clock.Now, null, null, null), incident, clock);
        ApplyOverWire(new AddForceUnitCommand(op, "Aich", 9, "Aich 42/1", "Im Einsatz", null, 4), incident, clock);
        ApplyOverWire(new AddScbaTruppCommand("Angriffstrupp",
            new[] { new TruppMemberDto(TruppRole.Truppfuehrer, "Müller"), new TruppMemberDto(TruppRole.Truppmann, "Schmidt") },
            "AT-1", null, 30, 60, 5), incident, clock);
        ApplyOverWire(new StartScbaTruppCommand(incident.ScbaTrupps.Last().Id, 300), incident, clock);

        Assert.True(incident.ChecklistAufbau[0].IsDone);
        Assert.Single(incident.Roles);
        Assert.Equal(9, incident.TotalPersonnel);
        var trupp = Assert.Single(incident.ScbaTrupps);
        Assert.Equal(300, trupp.StartPressure);
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
}
