using LageBuch.Domain.Files;

namespace LageBuch.Domain.Tests;

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
    public void AddFile_seeds_the_display_name_from_the_file_name()
    {
        var incident = NewIncident(out var clock, out var op);

        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);

        Assert.Equal("brand.jpg", file.DisplayName);
    }

    [Fact]
    public void RenameFile_changes_the_display_name_without_touching_the_journal()
    {
        var incident = NewIncident(out var clock, out var op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);
        var journalCountBefore = incident.Journal.Count;

        var renamed = incident.RenameFile(file.Id, "Küchenbrand, Erdgeschoss");

        Assert.Equal("Küchenbrand, Erdgeschoss", renamed.DisplayName);
        Assert.Equal("brand.jpg", renamed.FileName); // the original name never changes
        Assert.Same(renamed, Assert.Single(incident.Files));
        Assert.Equal(journalCountBefore, incident.Journal.Count); // silent — no ETB entry
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RenameFile_with_a_blank_name_resets_to_the_original_file_name(string? blank)
    {
        var incident = NewIncident(out var clock, out var op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);
        incident.RenameFile(file.Id, "Küchenbrand");

        var reset = incident.RenameFile(file.Id, blank);

        Assert.Equal("brand.jpg", reset.DisplayName);
    }

    [Fact]
    public void RenameFile_on_a_closed_incident_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.RenameFile(file.Id, "Neu"));
    }

    [Fact]
    public void RenameFile_with_an_unknown_id_throws()
    {
        var incident = NewIncident(out _, out _);

        Assert.Throws<KeyNotFoundException>(() => incident.RenameFile(Guid.NewGuid(), "Neu"));
    }

    [Fact]
    public void AddFile_on_a_closed_incident_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AddFile(clock, op, "x.pdf", "application/pdf", 100));
    }

    [Fact]
    public void RemoveFile_removes_the_file_and_logs_to_the_journal()
    {
        var incident = NewIncident(out var clock, out var op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);

        var removed = incident.RemoveFile(clock, op, file.Id);

        Assert.Same(file, removed);
        Assert.Empty(incident.Files);
        Assert.Equal("Datei entfernt: brand.jpg", incident.Journal[^1].Text);
        Assert.Equal(Etb.EtbDirection.System, incident.Journal[^1].Direction);
    }

    [Fact]
    public void RemoveFile_on_a_closed_incident_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.RemoveFile(clock, op, file.Id));
    }

    [Fact]
    public void RemoveFile_with_an_unknown_id_throws()
    {
        var incident = NewIncident(out var clock, out var op);

        Assert.Throws<KeyNotFoundException>(() => incident.RemoveFile(clock, op, Guid.NewGuid()));
    }

    [Fact]
    public void RemoveFile_logs_the_original_file_name_even_after_a_rename()
    {
        var incident = NewIncident(out var clock, out var op);
        var file = incident.AddFile(clock, op, "brand.jpg", "image/jpeg", 1024);
        incident.RenameFile(file.Id, "Küchenbrand, Erdgeschoss");

        incident.RemoveFile(clock, op, file.Id);

        Assert.Equal("Datei entfernt: brand.jpg", incident.Journal[^1].Text);
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

    // A joined sync client picks the file name (AddFileCommand), and every peer later writes those
    // bytes to a temp file under that name before handing it to the OS — so a name carrying path
    // segments must never survive the domain.
    [Theory]
    [InlineData("../../evil.png", "evil.png")]
    [InlineData("..\\evil.png", "evil.png")]
    [InlineData("dir/evil.png", "evil.png")]
    [InlineData("/etc/cron.d/evil.png", "evil.png")]
    [InlineData("..\\..\\..\\Startup\\evil.png", "evil.png")]
    [InlineData("  ../evil.png  ", "evil.png")]
    public void Create_keeps_only_the_last_path_segment_of_a_hostile_file_name(string hostile, string expected)
    {
        var file = IncidentFile.Create(hostile, "image/png", 1024, T0, "Müller");

        Assert.Equal(expected, file.FileName);
        Assert.Equal(expected, file.DisplayName);
    }

    // The host may run Windows and the client Linux (or the other way round): the same name has to
    // come out the same on both, so the Windows-invalid set is stripped regardless of the OS.
    [Fact]
    public void Create_strips_characters_that_are_invalid_in_a_file_name_on_any_platform()
    {
        var file = IncidentFile.Create("br<a>n:d\"|?*.jpg", "image/jpeg", 1024, T0, "Müller");

        Assert.Equal("brand.jpg", file.FileName);
    }

    // A Windows drive-relative name: Path.GetFileName would answer differently on Windows than on
    // Linux, so the colon is stripped like any other Windows-invalid character instead — the host
    // and every joined client end up with the same name whatever they run.
    [Fact]
    public void Create_treats_a_drive_relative_name_the_same_on_every_platform()
    {
        var file = IncidentFile.Create("C:evil.png", "image/png", 1024, T0, "Müller");

        Assert.Equal("Cevil.png", file.FileName);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("..\\")]
    [InlineData("<>|?*")]
    public void Create_rejects_a_file_name_that_leaves_nothing_usable(string hostile)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            IncidentFile.Create(hostile, "image/png", 1024, T0, "Müller"));
        Assert.Equal("fileName", ex.ParamName);
    }

    // Rehydrate is the load path (SQLite and a host's snapshot), so it sanitises the same way but
    // must never throw — a hostile name already on disk still has to open.
    [Theory]
    [InlineData("../../evil.png", "evil.png")]
    [InlineData("..\\evil.png", "evil.png")]
    [InlineData("dir/evil.png", "evil.png")]
    public void Rehydrate_sanitises_a_hostile_file_name(string hostile, string expected)
    {
        var file = IncidentFile.Rehydrate(
            Guid.NewGuid(), hostile, "Küchenbrand", "image/png", 1024, T0, "Müller");

        Assert.Equal(expected, file.FileName);
        Assert.Equal("Küchenbrand", file.DisplayName); // a free-form label, left alone
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData("<>|?*")]
    [InlineData("  ")]
    public void Rehydrate_falls_back_to_the_storage_style_name_instead_of_throwing(string hostile)
    {
        var id = Guid.NewGuid();

        var file = IncidentFile.Rehydrate(id, hostile, "Küchenbrand", "image/png", 1024, T0, "Müller");

        Assert.Equal($"{id}", file.FileName); // StorageFileName's shape — no extension left to keep
    }

    // Stripping the invalid characters can leave a bare extension; that is a usable, traversal-free
    // name, so it is kept rather than replaced by the fallback.
    [Fact]
    public void Rehydrate_keeps_a_name_that_strips_down_to_a_bare_extension()
    {
        var file = IncidentFile.Rehydrate(
            Guid.NewGuid(), "../../<>|.png", "Küchenbrand", "image/png", 1024, T0, "Müller");

        Assert.Equal(".png", file.FileName);
    }

    [Fact]
    public void WithDisplayName_returns_a_new_instance_leaving_the_original_untouched()
    {
        var original = IncidentFile.Create("brand.jpg", "image/jpeg", 1024, T0, "Müller");

        var renamed = original.WithDisplayName("Küchenbrand");

        Assert.Equal("brand.jpg", original.DisplayName); // unchanged
        Assert.Equal("Küchenbrand", renamed.DisplayName);
        Assert.Equal(original.Id, renamed.Id);
    }

    [Fact]
    public void StorageFileName_derives_from_id_and_original_extension()
    {
        var id = Guid.NewGuid();
        Assert.Equal($"{id}.jpg", IncidentFile.StorageFileName(id, "brand.jpg"));
        Assert.Equal($"{id}.PDF", IncidentFile.StorageFileName(id, "Bericht.PDF"));
    }

    [Fact]
    public void AllowedContentTypes_matches_the_mime_table_exactly()
    {
        Assert.Equal(
            new HashSet<string>(IncidentFile.MimeTypesByExtension.Values, StringComparer.OrdinalIgnoreCase),
            IncidentFile.AllowedContentTypes);
        Assert.Equal(5, IncidentFile.AllowedContentTypes.Count);
    }

    [Theory]
    [InlineData("brand.jpg", "image/jpeg")]
    [InlineData("brand.JPG", "image/jpeg")]
    [InlineData("brand.jpeg", "image/jpeg")]
    [InlineData("brand.png", "image/png")]
    [InlineData("brand.gif", "image/gif")]
    [InlineData("brand.webp", "image/webp")]
    [InlineData("bericht.pdf", "application/pdf")]
    [InlineData("bericht.PDF", "application/pdf")]
    public void GetMimeType_recognizes_every_allowed_extension_case_insensitively(string path, string expected)
    {
        Assert.Equal(expected, IncidentFile.GetMimeType(path, "fallback"));
    }

    [Fact]
    public void GetMimeType_returns_the_callers_fallback_for_an_unknown_extension()
    {
        Assert.Equal("application/octet-stream", IncidentFile.GetMimeType("notes.txt", "application/octet-stream"));
        Assert.Equal("*/*", IncidentFile.GetMimeType("notes.txt", "*/*"));
    }
}
