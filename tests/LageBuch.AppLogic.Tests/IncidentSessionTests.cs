using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Domain.Time;

namespace LageBuch.AppLogic.Tests;

public class IncidentSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void StartNew_seeds_checklist_saves_and_is_editable()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            new[] { ("A?", false), ("B?", false) },
            Array.Empty<(string, bool)>());

        Assert.False(session.IsReadOnly);
        Assert.Equal(2, session.Incident.ChecklistAufbau.Count);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Save_persists_to_the_session_path()
    {
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.Save();
        Assert.Equal(2, store.SaveCount); // start + explicit save
        Assert.Equal(session.Incident.Id, store.Load("/x.fwincident").Id);
    }

    [Fact]
    public void Close_closes_and_saves_and_becomes_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
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
        var session = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.fwincident",
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
    public async Task GetFileBytesAsync_returns_null_for_an_unknown_file()
    {
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        Assert.Null(await session.GetFileBytesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddFileAsync_raises_Changed()
    {
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
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
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Close();

        var reopened = LocalIncidentSession.Open(store, clock, "/x.fwincident", op: null);
        Assert.True(reopened.IsReadOnly);
    }

    [Fact]
    public void Open_editable_incident_requires_operator()
    {
        var store = new FakeStore();
        LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        Assert.Throws<InvalidOperationException>(() => LocalIncidentSession.Open(store, new FixedClock(T0), "/x.fwincident", op: null));
    }

    [Fact]
    public void OpenReadOnly_open_incident_is_readonly_without_operator()
    {
        var store = new FakeStore();
        LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.fwincident");

        Assert.True(ro.IsReadOnly);
        Assert.Null(ro.Operator);
        Assert.Equal(IncidentState.Open, ro.Incident.State);
    }

    [Fact]
    public void OpenReadOnly_closed_incident_is_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Close();

        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");
        Assert.True(ro.IsReadOnly);
    }

    [Fact]
    public void ContinueEditing_on_open_session_sets_operator_and_makes_editable()
    {
        var store = new FakeStore();
        LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.fwincident");

        ro.ContinueEditing(new SessionOperator("Schmidt"));

        Assert.False(ro.IsReadOnly);
        Assert.Equal("Schmidt", ro.Operator!.Display);
    }

    [Fact]
    public void ContinueEditing_on_closed_session_throws()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = LocalIncidentSession.StartNew(
            store,
            clock,
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        seed.Close();
        var ro = LocalIncidentSession.OpenReadOnly(store, clock, "/x.fwincident");

        Assert.Throws<InvalidOperationException>(
            () => ro.ContinueEditing(new SessionOperator("Schmidt")));
    }

    [Fact]
    public void ContinueEditing_is_idempotent_when_editable()
    {
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        session.ContinueEditing(new SessionOperator("Schmidt"));

        Assert.Equal("Müller", session.Operator!.Display); // unchanged
    }

    [Fact]
    public void ContinueEditing_appends_resumed_audit_event_with_operator_display()
    {
        var store = new FakeStore();
        LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.fwincident");

        ro.ContinueEditing(new SessionOperator("Schmidt", "FFB 1"));

        var resumed = Assert.Single(ro.Incident.Audit, a => a.Action == "resumed");
        Assert.Equal("Schmidt (FFB 1)", resumed.By);
    }

    [Fact]
    public void IsReadOnly_true_exactly_when_operator_is_null()
    {
        var store = new FakeStore();
        var editable = LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        Assert.False(editable.IsReadOnly); // has operator

        var ro = LocalIncidentSession.OpenReadOnly(store, new FixedClock(T0), "/x.fwincident");
        Assert.True(ro.IsReadOnly); // no operator
    }

    [Fact]
    public async Task ExportPdfAsync_returns_a_pdf()
    {
        var store = new FakeStore();
        var session = LocalIncidentSession.StartNew(
            store,
            new FixedClock(T0),
            new SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        var bytes = await session.ExportPdfAsync();
        Assert.True(bytes.Length > 100);
        Assert.Equal(0x25, bytes[0]); // %
    }
}

// In-memory fake store — no disk, deterministic.
internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();

    public int SaveCount { get; private set; }

    public void Save(string path, Incident incident)
    {
        _saved[path] = incident;
        SaveCount++;
    }

    public Task FlushAsync() => Task.CompletedTask;

    public Incident Load(string path) => _saved[path];

    public IncidentState? TryReadState(string path) => _saved.TryGetValue(path, out var i) ? i.State : null;

    private readonly Dictionary<string, byte[]> _files = new();

    public void SaveFileBytes(string path, string storageFileName, byte[] bytes) => _files[$"{path}/{storageFileName}"] = bytes;

    public byte[]? TryReadFileBytes(string path, string storageFileName) => _files.TryGetValue($"{path}/{storageFileName}", out var b) ? b : null;

    public event Action<Exception>? SaveFailed
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
