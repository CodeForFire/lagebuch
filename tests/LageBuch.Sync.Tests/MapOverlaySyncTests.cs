using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.Sync.Tests;

public class MapOverlaySyncTests
{
    private static readonly OperatorDto Peer = new("Bauer", "FFB 3");
    private static readonly DateTimeOffset First = new(2026, 3, 14, 9, 0, 0, TimeSpan.Zero);

    private static AddMapOverlayCommand Import(Guid? overlayId = null, Guid? fileId = null, string fileName = "prognose.zip", string contentType = "application/zip", int frames = 8) =>
        new(Peer, overlayId ?? Guid.NewGuid(), fileId ?? Guid.NewGuid(), fileName, contentType, 4096, "prognose", frames, First, First.AddMinutes(105));

    [Fact]
    public void An_Ebene_imported_on_a_client_reaches_the_host_with_its_file_and_is_attributed()
    {
        var (incident, clock) = NewIncident();
        var command = Import();

        var file = ApplyOverWire(command, incident, clock);

        var overlay = Assert.Single(incident.MapOverlays);
        Assert.Equal(command.OverlayId, overlay.Id);
        Assert.Equal(command.FileId, overlay.FileId);
        Assert.Equal(8, overlay.FrameCount);
        Assert.Equal(command.FileId, file?.Id);
        Assert.Equal("Bauer (FFB 3)", incident.Journal[^1].EnteredBy);
    }

    [Fact]
    public void Removing_an_Ebene_on_a_client_hands_the_host_the_file_to_wipe()
    {
        var (incident, clock) = NewIncident();
        var command = Import();
        ApplyOverWire(command, incident, clock);

        var removed = ApplyOverWire(new RemoveMapOverlayCommand(Peer, command.OverlayId), incident, clock);

        Assert.Equal(command.FileId, removed?.Id);
        Assert.Empty(incident.MapOverlays);
        Assert.Empty(incident.Files);
    }

    [Theory]
    [InlineData("prognose.hta", "application/zip", 8)]
    [InlineData("prognose.zip", "text/html", 8)]
    [InlineData("prognose.zip", "application/zip", 0)]
    [InlineData("prognose.zip", "application/zip", 100_000)]
    public void A_peer_s_Ebene_that_breaks_the_rules_is_refused_and_changes_nothing(string fileName, string contentType, int frames)
    {
        var (incident, clock) = NewIncident();
        var entries = incident.Journal.Count;

        Assert.Throws<ArgumentException>(() => ApplyOverWire(Import(fileName: fileName, contentType: contentType, frames: frames), incident, clock));

        Assert.Empty(incident.MapOverlays);
        Assert.Empty(incident.Files);
        Assert.Equal(entries, incident.Journal.Count);
    }

    [Fact]
    public void A_peer_reusing_a_file_id_already_in_the_Einsatz_is_refused()
    {
        var (incident, clock) = NewIncident();
        var file = incident.AddFile(clock, new SessionOperator("Host", "FFB 1"), "lageplan.png", "image/png", 100);

        Assert.Throws<ArgumentException>(() => ApplyOverWire(Import(fileId: file.Id), incident, clock));
        Assert.Empty(incident.MapOverlays);
    }

    [Fact]
    public void A_joining_client_sees_every_Ebene()
    {
        var (incident, clock) = NewIncident();
        ApplyOverWire(Import(), incident, clock);

        var json = SyncJson.Serialize(SnapshotMapper.ToSnapshot(incident));
        var copy = SnapshotMapper.FromSnapshot(SyncJson.Deserialize<IncidentSnapshot>(json));

        Assert.Equal(incident.MapOverlays, copy.MapOverlays);
    }

    [Fact]
    public void A_snapshot_without_Ebenen_reads_as_none()
    {
        var (incident, _) = NewIncident();
        var snapshot = SnapshotMapper.ToSnapshot(incident) with { MapOverlays = null };

        Assert.Empty(SnapshotMapper.FromSnapshot(snapshot).MapOverlays);
    }

    private static (Incident Incident, FixedClock Clock) NewIncident()
    {
        var clock = new FixedClock();
        return (Incident.Start(clock, new SessionOperator("Host", "FFB 1"), keyword: null, incidentNumber: null), clock);
    }

    private static Domain.Files.IncidentFile? ApplyOverWire(SyncCommand command, Incident incident, IClock clock) =>
        CommandApplier.Apply(SyncJson.Deserialize<SyncCommand>(SyncJson.Serialize(command)), incident, clock);
}
