using LageBuch.Domain.Etb;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Domain.Tests;

public class RehydrationTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void ChecklistItem_rehydrate_restores_state()
    {
        var id = Guid.NewGuid();
        var item = ChecklistItem.Rehydrate(id, "Blaulicht aus?", isDone: true, note: "ok", isMandatory: true);
        Assert.Equal(id, item.Id);
        Assert.True(item.IsDone);
        Assert.Equal("ok", item.Note);
        Assert.True(item.IsMandatory);
    }

    [Fact]
    public void EtbEntry_rehydrate_preserves_id_and_enteredby()
    {
        var id = Guid.NewGuid();
        var entry = EtbEntry.Rehydrate(id, T0, EtbDirection.Incoming, "Meldung", "Müller (FFB 12/1)", "ILS", null);
        Assert.Equal(id, entry.Id);
        Assert.Equal(T0, entry.Timestamp);
        Assert.Equal("Müller (FFB 12/1)", entry.EnteredBy);
        Assert.Equal("ILS", entry.From);
    }

    [Fact]
    public void EtbEntry_rehydrate_without_edits_arg_defaults_to_empty()
    {
        var entry = EtbEntry.Rehydrate(Guid.NewGuid(), T0, EtbDirection.Incoming, "Meldung", "Müller", "ILS", null);
        Assert.Empty(entry.Edits);
    }

    [Fact]
    public void EtbEntry_rehydrate_with_edits_preserves_history()
    {
        var edits = new[]
        {
            new EtbEntryEdit("Original", "Müller", T0),
            new EtbEntryEdit("Zweite Fassung", "Schmidt", T0.AddMinutes(5)),
        };

        var entry = EtbEntry.Rehydrate(
            Guid.NewGuid(), T0, EtbDirection.Incoming, "Dritte Fassung", "Müller", "ILS", null, edits);

        Assert.Equal(edits, entry.Edits);
    }

    [Fact]
    public void Incident_rehydrate_restores_closed_incident_fully()
    {
        var id = Guid.NewGuid();
        var entry = EtbEntry.Rehydrate(Guid.NewGuid(), T0, EtbDirection.Internal, "x", "Müller", null, null);
        var incident = Incident.Rehydrate(
            id, T0, IncidentState.Closed,
            new IncidentNumber("B 1.2 260715 4242"),
            "Brand", "Hauptstr. 1", "FFB", "abgearbeitet",
            T0.AddHours(2), "Müller",
            new[] { ChecklistItem.Rehydrate(Guid.NewGuid(), "c", false, null, isMandatory: false) },
            Array.Empty<ChecklistItem>(),
            new[] { entry },
            new[] { RoleAssignment.Create("EL", "Müller") },
            new[] { ForceUnit.Create("FFB", 12) },
            Array.Empty<Atemschutz.AtemschutzTrupp>(),
            new[] { new AuditEvent(T0, "opened", "Müller") },
            Array.Empty<Time.IncidentTimerState>(),
            Array.Empty<Files.IncidentFile>());

        Assert.Equal(id, incident.Id);
        Assert.Equal(IncidentState.Closed, incident.State);
        Assert.Equal("B 1.2 260715 4242", incident.IncidentNumber!.Value);
        Assert.Equal(T0.AddHours(2), incident.ClosedAt);
        Assert.Single(incident.Journal);
        Assert.Equal(12, incident.TotalPersonnel);
    }

    [Fact]
    public void IncidentFile_rehydrate_restores_metadata()
    {
        var id = Guid.NewGuid();
        var file = Files.IncidentFile.Rehydrate(id, "brand.jpg", "Küchenbrand", "image/jpeg", 2048, T0, "Müller (FFB 12/1)");
        Assert.Equal(id, file.Id);
        Assert.Equal("brand.jpg", file.FileName);
        Assert.Equal("Küchenbrand", file.DisplayName);
        Assert.Equal(2048, file.SizeBytes);
    }

    [Fact]
    public void Incident_rehydrate_carries_files()
    {
        var fileId = Guid.NewGuid();
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), T0, IncidentState.Open, null, null, null, null, null, null, null,
            Array.Empty<ChecklistItem>(), Array.Empty<ChecklistItem>(), Array.Empty<EtbEntry>(),
            Array.Empty<RoleAssignment>(), Array.Empty<ForceUnit>(),
            Array.Empty<Atemschutz.AtemschutzTrupp>(), Array.Empty<AuditEvent>(),
            Array.Empty<Time.IncidentTimerState>(),
            new[] { Files.IncidentFile.Rehydrate(fileId, "bericht.pdf", "bericht.pdf", "application/pdf", 4096, T0, "Müller") });

        var file = Assert.Single(incident.Files);
        Assert.Equal(fileId, file.Id);
        Assert.Equal("bericht.pdf", file.FileName);
    }

    [Fact]
    public void Rehydrated_closed_incident_rejects_mutation()
    {
        var incident = Incident.Rehydrate(
            Guid.NewGuid(), T0, IncidentState.Closed,
            null, null, null, null, null, T0, "Müller",
            Array.Empty<ChecklistItem>(), Array.Empty<ChecklistItem>(), Array.Empty<EtbEntry>(),
            Array.Empty<RoleAssignment>(), Array.Empty<ForceUnit>(),
            Array.Empty<Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<Time.IncidentTimerState>(),
            Array.Empty<Files.IncidentFile>());
        Assert.Throws<IncidentClosedException>(() => incident.SetStatus("x"));
    }
}
