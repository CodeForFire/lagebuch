using LageBuch.Domain;
using LageBuch.Domain.Time;
using Microsoft.Data.Sqlite;

namespace LageBuch.Persistence.Tests;

// The seal of a closed Einsatz (#598) lives in the incident file: the hash on incident_meta, each
// attachment's SHA-256 on its incident_files row.
public class IncidentSealPersistenceTests : IDisposable
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string PhotoSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly string _path = Path.Join(Path.GetTempPath(), $"seal-{Guid.NewGuid():N}.fwincident");

    private sealed class Clock : IClock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void A_sealed_incident_round_trips_its_seal_and_every_attachment_hash()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        var photo = incident.AddFile(clock, op, "foto.jpg", "image/jpeg", 3);
        var lost = incident.AddFile(clock, op, "skizze.png", "image/png", 4);
        incident.Close(clock, op);
        incident.ApplySeal(new IncidentSeal(
            IncidentSeal.CurrentVersion,
            Hash,
            new Dictionary<Guid, string?> { [photo.Id] = PhotoSha, [lost.Id] = null }));

        IncidentRepository.Save(_path, incident);
        var loaded = IncidentRepository.Load(_path);

        var seal = Assert.IsType<IncidentSeal>(loaded.Seal);
        Assert.Equal(IncidentSeal.CurrentVersion, seal.Version);
        Assert.Equal(Hash, seal.Hash);
        Assert.Equal(PhotoSha, seal.FileSha256[photo.Id]);
        Assert.True(seal.FileSha256.TryGetValue(lost.Id, out var lostSha));
        Assert.Null(lostSha);
    }

    [Fact]
    public void A_closed_incident_from_before_the_seal_loads_without_one()
    {
        var clock = new Clock();
        var op = new SessionOperator("Müller");
        var incident = Incident.Start(clock, op);
        incident.AddFile(clock, op, "foto.jpg", "image/jpeg", 3);
        incident.Close(clock, op);

        IncidentRepository.Save(_path, incident);

        Assert.Null(IncidentRepository.Load(_path).Seal);
    }
}
