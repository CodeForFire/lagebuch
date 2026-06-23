using Feuerwehr.AppLogic.Services;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.AppLogic.Tests;

// In-memory fake store — no disk, deterministic.
internal sealed class FakeStore : IIncidentStore
{
    private readonly Dictionary<string, Incident> _saved = new();
    public int SaveCount { get; private set; }
    public void Save(string path, Incident incident) { _saved[path] = incident; SaveCount++; }
    public Incident Load(string path) => _saved[path];
}

internal sealed class FixedClock : IClock
{
    public FixedClock(DateTimeOffset now) => Now = now;
    public DateTimeOffset Now { get; set; }
}

public class IncidentSessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void StartNew_seeds_checklist_saves_and_is_editable()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", new[] { "A?", "B?" });

        Assert.False(session.IsReadOnly);
        Assert.Equal(2, session.Incident.Checklist.Count);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public void Save_persists_to_the_session_path()
    {
        var store = new FakeStore();
        var session = IncidentSession.StartNew(store, new FixedClock(T0), new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<string>());
        session.Save();
        Assert.Equal(2, store.SaveCount); // start + explicit save
        Assert.Equal(session.Incident.Id, store.Load("/x.fwincident").Id);
    }

    [Fact]
    public void Close_closes_and_saves_and_becomes_readonly()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var session = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<string>());
        clock.Now = T0.AddHours(1);
        session.Close(clock);

        Assert.True(session.IsReadOnly);
        Assert.Equal(IncidentState.Closed, session.Incident.State);
    }

    [Fact]
    public void Open_closed_incident_is_readonly_without_operator()
    {
        var store = new FakeStore();
        var clock = new FixedClock(T0);
        var seed = IncidentSession.StartNew(store, clock, new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<string>());
        seed.Close(clock);

        var reopened = IncidentSession.Open(store, "/x.fwincident", op: null);
        Assert.True(reopened.IsReadOnly);
    }

    [Fact]
    public void Open_editable_incident_requires_operator()
    {
        var store = new FakeStore();
        IncidentSession.StartNew(store, new FixedClock(T0), new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => IncidentSession.Open(store, "/x.fwincident", op: null));
    }

    [Fact]
    public void ExportPdf_returns_a_pdf()
    {
        var store = new FakeStore();
        var session = IncidentSession.StartNew(store, new FixedClock(T0), new SessionOperator("Müller"),
            "/x.fwincident", Array.Empty<string>());
        var bytes = session.ExportPdf();
        Assert.True(bytes.Length > 100);
        Assert.Equal(0x25, bytes[0]); // %
    }
}
