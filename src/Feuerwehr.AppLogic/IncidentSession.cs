using Feuerwehr.AppLogic.Services;
using Feuerwehr.Documents;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;

namespace Feuerwehr.AppLogic;

public sealed class IncidentSession
{
    private readonly IIncidentStore _store;

    private IncidentSession(IIncidentStore store, Incident incident, string path, SessionOperator? op)
    {
        _store = store;
        Incident = incident;
        Path = path;
        Operator = op;
    }

    public Incident Incident { get; }
    public string Path { get; }
    public SessionOperator? Operator { get; }
    public bool IsReadOnly => Incident.State == IncidentState.Closed;

    public static IncidentSession StartNew(
        IIncidentStore store,
        IClock clock,
        SessionOperator op,
        string path,
        IEnumerable<string> checklistTemplate)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(op);
        var incident = Incident.Start(clock, op);
        incident.SeedChecklist(checklistTemplate);
        var session = new IncidentSession(store, incident, path, op);
        session.Save();
        return session;
    }

    public static IncidentSession Open(IIncidentStore store, string path, SessionOperator? op)
    {
        ArgumentNullException.ThrowIfNull(store);
        var incident = store.Load(path);
        if (incident.State == IncidentState.Open && op is null)
            throw new InvalidOperationException("Ein offener Einsatz erfordert einen Bearbeiter.");
        var effectiveOperator = incident.State == IncidentState.Closed ? null : op;
        return new IncidentSession(store, incident, path, effectiveOperator);
    }

    public void Save() => _store.Save(Path, Incident);

    public void Close(IClock clock)
    {
        if (IsReadOnly)
            throw new InvalidOperationException("Der Einsatz ist bereits abgeschlossen.");
        if (Operator is null)
            throw new InvalidOperationException("Kein Bearbeiter für den Abschluss vorhanden.");
        Incident.Close(clock, Operator);
        Save();
    }

    public byte[] ExportPdf() => IncidentPdf.Generate(Incident);
}
