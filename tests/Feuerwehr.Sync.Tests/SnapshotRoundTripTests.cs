using Feuerwehr.Domain;
using Feuerwehr.Domain.Atemschutz;
using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Sync.Tests;

internal sealed class FixedClock : IClock
{
    public DateTimeOffset Now { get; set; } = new(2026, 8, 12, 9, 0, 0, TimeSpan.FromHours(2));
}

public class SnapshotRoundTripTests
{
    // Builds an incident that touches every collection the snapshot carries: checklist (toggled),
    // journal, roles, forces (added + updated), an SCBA trupp (registered → started → pressure →
    // returned, so members + readings are populated), address/keyword/status, and a close (so
    // ClosedAt/ClosedBy + audit are set).
    private static Incident BuildRichIncident()
    {
        var clock = new FixedClock();
        var op = new SessionOperator("Müller", "FFB 12/1");
        var incident = Incident.Start(clock, op, keyword: "Brand", incidentNumber: new IncidentNumber("B 1.2 260812 001"));
        incident.SeedChecklist(new[] { "Aufstellort ELW?", "Funk auf 0?" });
        incident.SetAddress("Hauptstraße 1", "Bezirk 2");
        incident.SetStatus("Im Einsatz");
        incident.SetKeyword("Brand 2 – Wohnhaus");

        clock.Now = clock.Now.AddMinutes(1);
        incident.AddJournalEntry(clock, op, EtbDirection.Incoming, "Erstmeldung", from: "Leitstelle", to: "ELW");
        incident.ToggleChecklistItem(incident.Checklist[0].Id);
        incident.AssignRole("EL", "Huber", callSign: "FFB 1", from: clock.Now, section: "Abschnitt 1", phone: "0171/1234567");

        var force = incident.AddForceUnit(clock, op, "Aich", personnelCount: 9, callSign: "Aich 42/1",
            status: "Auf Anfahrt", notes: "erste Welle", scbaCount: 4);
        incident.UpdateForceUnit(clock, op, force.Id, "Im Einsatz", "eingetroffen");

        var trupp = incident.AddScbaTrupp(clock, "Angriffstrupp", TruppMember.Crew("Müller", "Schmidt"),
            callSign: "AT-1", task: "Menschenrettung");
        clock.Now = clock.Now.AddMinutes(2);
        incident.StartScbaTrupp(clock, trupp.Id, startPressure: 300);
        clock.Now = clock.Now.AddMinutes(5);
        incident.RecordScbaPressure(clock, trupp.Id, bar: 250);
        clock.Now = clock.Now.AddMinutes(3);
        incident.MarkScbaReturned(clock, trupp.Id);

        clock.Now = clock.Now.AddMinutes(1);
        incident.Close(clock, op);
        return incident;
    }

    [Fact]
    public void Incident_survives_snapshot_json_rehydrate_unchanged()
    {
        var original = BuildRichIncident();

        var json = SyncJson.Serialize(SnapshotMapper.ToSnapshot(original));
        var rehydrated = SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(json));

        // The snapshot of the rehydrated incident must serialize identically to the original's —
        // proves every field/collection/order made the round trip through JSON.
        Assert.Equal(json, SyncJson.Serialize(SnapshotMapper.ToSnapshot(rehydrated)));
    }

    [Fact]
    public void Round_trip_preserves_key_fields_and_collection_contents()
    {
        var original = BuildRichIncident();
        var json = SyncJson.Serialize(SnapshotMapper.ToSnapshot(original));

        var r = SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(json));

        Assert.Equal(original.Id, r.Id);
        Assert.Equal(IncidentState.Closed, r.State);
        Assert.Equal("B 1.2 260812 001", r.IncidentNumber!.Value);
        Assert.Equal("Brand 2 – Wohnhaus", r.Keyword);
        Assert.Equal("Hauptstraße 1", r.Street);
        Assert.Equal(original.ClosedAt, r.ClosedAt);
        Assert.Equal(original.ClosedBy, r.ClosedBy);
        Assert.Equal(original.Checklist.Count, r.Checklist.Count);
        Assert.True(r.Checklist[0].IsDone);
        Assert.Equal(original.Journal.Count, r.Journal.Count);
        Assert.Equal(9, r.Forces[0].PersonnelCount);
        Assert.Equal("Im Einsatz", r.Forces[0].Status);

        var trupp = Assert.Single(r.ScbaTrupps);
        Assert.Equal(2, trupp.Members.Count);
        Assert.Equal(300, trupp.StartPressure);
        Assert.NotNull(trupp.ExitTime);
        Assert.Contains(trupp.PressureReadings, p => p.Bar == 250);
        Assert.NotEmpty(r.Audit);
    }
}
