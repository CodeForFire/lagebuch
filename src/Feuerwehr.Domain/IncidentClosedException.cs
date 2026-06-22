namespace Feuerwehr.Domain;

public sealed class IncidentClosedException : InvalidOperationException
{
    public IncidentClosedException()
        : base("Der Einsatz ist abgeschlossen und schreibgeschützt.") { }

    public IncidentClosedException(string message) : base(message) { }
}
