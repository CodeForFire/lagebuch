using Feuerwehr.Domain.Files;

namespace Feuerwehr.Domain.Tests;

public class FilesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static Incident NewIncident(out FixedClock clock, out SessionOperator op)
    {
        clock = new FixedClock(T0);
        op = new SessionOperator("Müller", "FFB 12/1");
        return Incident.Start(clock, op);
    }

    [Fact]
    public void AddFile_records_metadata_and_logs_to_the_journal()
    {
        var incident = NewIncident(out var clock, out var op);

        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);

        var recorded = Assert.Single(incident.Files);
        Assert.Same(file, recorded);
        Assert.Equal("brand.jpg", file.FileName);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(1024, file.SizeBytes);
        Assert.Equal(T0, file.AddedAt);
        Assert.Equal("Müller (FFB 12/1)", file.AddedBy);

        Assert.Equal("Datei hinzugefügt: brand.jpg", incident.Journal[^1].Text);
        Assert.Equal(Etb.EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void AddFile_on_a_closed_incident_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AddFile(clock, op, "x.pdf", "application/pdf", 100));
    }

    [Fact]
    public void Create_rejects_unsupported_content_type()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            IncidentFile.Create("x.txt", "text/plain", 100, T0, "Müller"));
        Assert.Equal("contentType", ex.ParamName);
    }

    [Fact]
    public void Create_rejects_a_file_over_the_size_cap()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            IncidentFile.Create("x.pdf", "application/pdf", IncidentFile.MaxSizeBytes + 1, T0, "Müller"));
        Assert.Equal("sizeBytes", ex.ParamName);
    }

    [Fact]
    public void StorageFileName_derives_from_id_and_original_extension()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"{id}.jpg", IncidentFile.StorageFileName(id, "brand.jpg"));
        Assert.Equal($"{id}.PDF", IncidentFile.StorageFileName(id, "Bericht.PDF"));
    }
}
