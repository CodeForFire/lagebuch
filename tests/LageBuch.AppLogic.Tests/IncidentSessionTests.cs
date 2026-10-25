using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Etb;
using LageBuch.Domain.Time;
using Microsoft.Data.Sqlite;

namespace LageBuch.AppLogic.Tests;

public class IncidentSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void StartNew_seeds_checklist_saves_and_is_editable()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            new[] { ("A?", false), ("B?", false) },
            Array.Empty<(string, bool)>());

        Assert.False(session.IsReadOnly);
        Assert.Equal(2, session.Incident.Checklists[0].Items.Count);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Save_persists_to_the_session_path()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Save();
        Assert.Equal(2, store.SaveCount); // start + explicit save
        Assert.Equal(session.Incident.Id, store.Load("/x.lagebuch").Id);
    }

    [Fact]
    public void Close_closes_and_saves_and_becomes_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        clock.Now = T0.AddHours(1);
        session.Close();

        Assert.True(session.IsReadOnly);
        Assert.Equal(IncidentState.Closed, session.Incident.State);
    }

    [Fact]
    public async Task AddFileAsync_records_it_writes_bytes_and_saves()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var savesBefore = store.SaveCount;

        var bytes = new byte[] { 1, 2, 3 };
        await session.AddFileAsync("brand.jpg", "image/jpeg", bytes);

        var file = Assert.Single(session.Incident.Files);
        Assert.Equal("brand.jpg", file.FileName);
        Assert.True(store.SaveCount > savesBefore);
        Assert.Equal(bytes, await session.GetFileBytesAsync(file.Id));
    }

    [Fact]
    public async Task AddFileAsync_does_not_block_the_caller_while_the_file_write_is_in_flight()
    {
        // issue #167 P1 #1: the file write must be genuinely async — not a synchronous write
        // masquerading as async behind Task.CompletedTask, which would have already finished (and
        // blocked whatever thread called AddFileAsync) by the time this method's Task is observed.
        var release = new TaskCompletionSource();
        var store = new DelayedFileWriteStore(release.Task);
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        var addTask = session.AddFileAsync("brand.jpg", "image/jpeg", new byte[] { 1, 2, 3 });

        Assert.False(addTask.IsCompleted); // still awaiting the (currently stuck) file write
        release.SetResult();
        await addTask;

        Assert.Single(session.Incident.Files);
    }

    [Fact]
    public async Task SaveFileStreamAsync_stores_the_streamed_bytes_under_the_storage_name()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await session.AddFileAsync("brand.jpg", "image/jpeg", new byte[] { 1 });
        var file = Assert.Single(session.Incident.Files);
        using var source = new MemoryStream(new byte[] { 7, 8, 9 });

        await session.SaveFileStreamAsync(LageBuch.Domain.Files.IncidentFile.StorageFileName(file.Id, file.FileName), source);

        Assert.Equal(new byte[] { 7, 8, 9 }, await session.GetFileBytesAsync(file.Id));
    }

    [Fact]
    public async Task GetFileBytesAsync_returns_null_for_an_unknown_file()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        Assert.Null(await session.GetFileBytesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddFileAsync_raises_Changed()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var raised = false;
        session.Changed += () => raised = true;

        await session.AddFileAsync("bericht.pdf", "application/pdf", new byte[] { 1 });

        Assert.True(raised);
    }

    [Fact]
    public void Open_closed_incident_is_readonly_without_operator()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Close();

        var reopened = LocalIncidentSession.Open(store, clock, "/x.lagebuch", op: null);
        Assert.True(reopened.IsReadOnly);
    }

    [Fact]
    public void Open_editable_incident_requires_operator()
    {
        var store = new FakeStore();
        TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        Assert.Throws<InvalidOperationException>(() => LocalIncidentSession.Open(store, new FixedClock(T0), "/x.lagebuch", op: null));
    }

    [Fact]
    public void OpenReadOnly_open_incident_is_readonly_without_operator()
    {
        var store = new FakeStore();
        TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");

        Assert.True(ro.IsReadOnly);
        Assert.Null(ro.Operator);
        Assert.Equal(IncidentState.Open, ro.Incident.State);
    }

    [Fact]
    public void OpenReadOnly_closed_incident_is_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");
        Assert.True(ro.IsReadOnly);
    }

    [Fact]
    public void ContinueEditing_on_open_session_sets_operator_and_makes_editable()
    {
        var store = new FakeStore();
        TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");

        ro.ContinueEditing(new SessionOperator("Schmidt"));

        Assert.False(ro.IsReadOnly);
        Assert.Equal("Schmidt", ro.Operator!.Display);
    }

    [Fact]
    public void ContinueEditing_on_closed_session_throws()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = TestSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Close();
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.lagebuch");

        Assert.Throws<InvalidOperationException>(
            () => ro.ContinueEditing(new SessionOperator("Schmidt")));
    }

    [Fact]
    public void ContinueEditing_is_idempotent_when_editable()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        session.ContinueEditing(new SessionOperator("Schmidt"));

        Assert.Equal("Müller", session.Operator!.Display); // unchanged
    }

    [Fact]
    public void ChangeOperator_credits_later_entries_to_the_new_operator_and_saves()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var saves = store.SaveCount;
        var changed = 0;
        session.Changed += () => changed++;

        session.ChangeOperator(new SessionOperator("Schmidt", "FFB 1"));
        session.AddJournalEntry(EtbDirection.Incoming, "Lagemeldung");

        Assert.Equal("Schmidt (FFB 1)", session.Operator!.Display);
        var handover = Assert.Single(session.Incident.Journal, e => e.Text == "Lagebuchführerwechsel: Müller → Schmidt (FFB 1)");
        Assert.Equal("Schmidt (FFB 1)", handover.EnteredBy);
        Assert.Equal("Schmidt (FFB 1)", session.Incident.Journal[^1].EnteredBy);
        Assert.True(store.SaveCount > saves);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void ChangeOperator_to_the_same_operator_logs_nothing()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller", "FFB 1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var entries = session.Incident.Journal.Count;
        var saves = store.SaveCount;

        session.ChangeOperator(new SessionOperator(" Müller ", "FFB 1"));

        Assert.Equal(entries, session.Incident.Journal.Count);
        Assert.Equal(saves, store.SaveCount);
    }

    [Fact]
    public void ChangeOperator_on_a_read_only_session_throws()
    {
        var store = new FakeStore();
        TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");

        Assert.Throws<InvalidOperationException>(() => ro.ChangeOperator(new SessionOperator("Schmidt")));
    }

    [Fact]
    public void ContinueEditing_appends_resumed_audit_event_with_operator_display()
    {
        var store = new FakeStore();
        TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");

        ro.ContinueEditing(new SessionOperator("Schmidt", "FFB 1"));

        var resumed = Assert.Single(ro.Incident.Audit, a => a.Action == "resumed");
        Assert.Equal("Schmidt (FFB 1)", resumed.By);
    }

    [Fact]
    public void IsReadOnly_true_exactly_when_operator_is_null()
    {
        var store = new FakeStore();
        var editable = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        Assert.False(editable.IsReadOnly); // has operator

        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.lagebuch");
        Assert.True(ro.IsReadOnly); // no operator
    }

    [Fact]
    public async Task ExportPdfAsync_returns_a_pdf()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var bytes = await session.ExportPdfAsync(new TestPdfExporter());
        Assert.True(bytes.Length > 100);
        Assert.Equal(0x25, bytes[0]); // %
    }

    [Fact]
    public async Task ExportPdfAsync_merges_an_attached_pdfs_pages()
    {
        // AddFileAsync stores the attachment; ExportPdfAsync reads it back through the store and
        // merges it via a temp copy.
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/y.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 1, 2, 3 };
        await session.AddFileAsync("bericht.pdf", "application/pdf", pdf);
        var fileId = Assert.Single(session.Incident.Files).Id;
        var exporter = new RecordingPdfExporter();

        await session.ExportPdfAsync(exporter);

        Assert.Equal(fileId, exporter.PdfAttachmentId);
        Assert.Equal(pdf, exporter.PdfAttachmentBytes);
    }

    [Fact]
    public async Task RemoveFileAsync_persists_the_metadata_removal_before_the_byte_delete_and_reports_its_failure()
    {
        var store = new FakeStore();
        var session = TestSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        await session.AddFileAsync("foto.jpg", "image/jpeg", new byte[] { 1, 2, 3 });
        var fileId = Assert.Single(session.Incident.Files).Id;
        var changed = 0;
        session.Changed += () => changed++;
        store.DeleteFailure = new IOException("wipe failed");
        var savesBefore = store.SaveCount;
        var flushesBefore = store.FlushCount;

        await Assert.ThrowsAsync<IOException>(() => session.RemoveFileAsync(fileId));

        Assert.Empty(session.Incident.Files);
        Assert.Equal(savesBefore + 1, store.SaveCountAtDelete); // saved before the delete was tried
        Assert.Equal(flushesBefore + 1, store.FlushCountAtDelete); // and written, not just queued
        Assert.Equal(1, changed); // the domain changed, so views refresh despite the failure
    }

    [Fact]
    public async Task An_attachment_added_right_after_a_new_incident_starts_is_stored()
    {
        var directory = Directory.CreateTempSubdirectory("lagebuch-session-").FullName;
        try
        {
            var path = Path.Join(directory, "einsatz.lagebuch");
            var store = new IncidentStore();
            var session = TestSession.StartNew(
                store,
                new FixedClock(T0),
                new SessionOperator("Müller"),
                path,
                Array.Empty<(string, bool)>(),
                Array.Empty<(string, bool)>());
            var bytes = new byte[] { 9, 8, 7, 6 };

            await session.AddFileAsync("foto.jpg", "image/jpeg", bytes);

            var file = Assert.Single(session.Incident.Files);
            Assert.Equal(bytes, await session.GetFileBytesAsync(file.Id));
            Assert.False(Directory.Exists(Path.Join(directory, "einsatz.files")));
            await store.FlushAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Host_pdf_export_still_merges_a_pdf_attachment()
    {
        var directory = Directory.CreateTempSubdirectory("lagebuch-session-").FullName;
        try
        {
            var path = Path.Join(directory, "einsatz.lagebuch");
            var store = new IncidentStore();
            var session = TestSession.StartNew(
                store,
                new FixedClock(T0),
                new SessionOperator("Müller"),
                path,
                Array.Empty<(string, bool)>(),
                Array.Empty<(string, bool)>());
            var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 4, 5, 6 };
            await session.AddFileAsync("bericht.pdf", "application/pdf", pdf);
            var fileId = Assert.Single(session.Incident.Files).Id;
            var exporter = new RecordingPdfExporter();

            await session.ExportPdfAsync(exporter);

            Assert.Equal(fileId, exporter.PdfAttachmentId);
            Assert.Equal(pdf, exporter.PdfAttachmentBytes);
            await store.FlushAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}

// Records what the export hands the renderer for a PDF attachment. The bytes are read inside
// Generate because the export deletes its temp copy as soon as Generate returns.
internal sealed class RecordingPdfExporter : IIncidentPdfExporter
{
    public bool CanExport => true;

    public Guid? PdfAttachmentId { get; private set; }

    public byte[]? PdfAttachmentBytes { get; private set; }

    public byte[] Generate(Incident incident, DateTimeOffset asOf, IReadOnlyDictionary<Guid, byte[]> fileBytes, IReadOnlyDictionary<Guid, string> pdfAttachmentPaths, IncidentPdfSections sections = IncidentPdfSections.All)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(pdfAttachmentPaths);
        foreach (var (id, path) in pdfAttachmentPaths)
        {
            PdfAttachmentId = id;
            PdfAttachmentBytes = File.ReadAllBytes(path);
        }

        return new byte[] { 0x25 };
    }
}

// In-memory fake store — no disk, deterministic.
internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();

    public int SaveCount { get; private set; }

    /// <summary>When set, <see cref="DeleteFileBytesAsync"/> throws it, like a store whose wipe failed.</summary>
    public Exception? DeleteFailure { get; set; }

    /// <summary><see cref="SaveCount"/> as it stood when the byte delete was attempted.</summary>
    public int SaveCountAtDelete { get; private set; }

    public int FlushCount { get; private set; }

    /// <summary><see cref="FlushCount"/> as it stood when the byte delete was attempted.</summary>
    public int FlushCountAtDelete { get; private set; }

    public void Save(string path, Incident incident)
    {
        _saved[path] = incident;
        SaveCount++;
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        FlushCount++;
        return Task.CompletedTask;
    }

    public Incident Load(string path) => _saved[path];

    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    private readonly Dictionary<string, byte[]> _files = new();

    public Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _files[$"{path}/{storageFileName}"] = bytes;
        return Task.CompletedTask;
    }

    public async Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await source.CopyToAsync(ms, cancellationToken);
        await SaveFileBytesAsync(path, storageFileName, ms.ToArray(), cancellationToken);
    }

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult(_files.TryGetValue($"{path}/{storageFileName}", out var b) ? b : null);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default)
    {
        SaveCountAtDelete = SaveCount;
        FlushCountAtDelete = FlushCount;
        if (DeleteFailure is not null)
        {
            return Task.FromException(DeleteFailure);
        }

        _files.Remove($"{path}/{storageFileName}");
        return Task.CompletedTask;
    }

    // Real events, not the usual no-op double: IncidentWorkspaceViewModelTests raises these
    // directly to exercise the PersistenceError wiring without a real background writer thread.
    public event Action<Exception>? SaveFailed;

    public event Action? SaveSucceeded;

    public void RaiseSaveFailed(Exception ex) => SaveFailed?.Invoke(ex);

    public void RaiseSaveSucceeded() => SaveSucceeded?.Invoke();
}

// SaveFileBytesAsync doesn't complete until the caller-supplied gate task does — lets a test prove
// its caller isn't blocked synchronously waiting on the write (issue #167 P1 #1).
internal sealed class DelayedFileWriteStore : IIncidentStore
{
    private readonly Task _gate;
    private readonly Dictionary<string, Incident> _saved = new();

    public DelayedFileWriteStore(Task gate) => _gate = gate;

    public void Save(string path, Incident incident) => _saved[path] = incident;

    public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Incident Load(string path) => _saved[path];

    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    public async Task SaveFileBytesAsync(string path, string storageFileName, byte[] bytes, CancellationToken cancellationToken = default) =>
        await _gate;

    public async Task SaveFileStreamAsync(string path, string storageFileName, Stream source, CancellationToken cancellationToken = default) =>
        await _gate;

    public Task<byte[]?> TryReadFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(null);

    public Task DeleteFileBytesAsync(string path, string storageFileName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public event Action<Exception>? SaveFailed
    {
        add { }
        remove { }
    }

    public event Action? SaveSucceeded
    {
        add { }
        remove { }
    }
}

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }
}
