using Feuerwehr.AppLogic.Services;
using Feuerwehr.Documents;
using Feuerwehr.Domain;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;

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
    public SessionOperator? Operator { get; private set; }

    // Read-only when there is no operator to attribute edits to, OR the incident is closed
    // (closing keeps its operator for attribution, but a closed incident is irreversibly
    // read-only — the domain rejects mutations either way).
    public bool IsReadOnly => Operator is null || Incident.State == IncidentState.Closed;

    public static IncidentSession StartNew(
        IIncidentStore store,
        IClock clock,
        SessionOperator op,
        string path,
        IEnumerable<string> checklistTemplate,
        IlsNumber? ilsNumber = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(op);
        var incident = Incident.Start(clock, op);
        incident.SeedChecklist(checklistTemplate);
        if (ilsNumber is not null)
            incident.SetIlsNumber(ilsNumber);
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

    // Opens any incident read-only, regardless of state, without requiring an operator.
    // The workspace can later upgrade an open incident to editable via ContinueEditing.
    public static IncidentSession OpenReadOnly(IIncidentStore store, string path)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new IncidentSession(store, store.Load(path), path, op: null);
    }

    // Upgrades a read-only-opened, still-open incident to editable by attaching an operator.
    public void ContinueEditing(IClock clock, SessionOperator op)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(op);
        if (Incident.State == IncidentState.Closed)
            throw new InvalidOperationException("Ein abgeschlossener Einsatz kann nicht weiter bearbeitet werden.");
        if (Operator is not null)
            return; // already editable
        Operator = op;
        Incident.ResumeEditing(clock, op);
        Save();
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
