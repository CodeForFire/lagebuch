using Feuerwehr.Domain.Etb;
using Feuerwehr.Domain.Time;
using Feuerwehr.Domain.ValueObjects;

namespace Feuerwehr.Domain;

public sealed record AuditEvent(DateTimeOffset At, string Action, string By);

public sealed class Incident
{
    private readonly List<ChecklistItem> _checklist = new();
    private readonly List<EtbEntry> _journal = new();
    private readonly List<RoleAssignment> _roles = new();
    private readonly List<ForceUnit> _forces = new();
    private readonly List<AuditEvent> _audit = new();

    private Incident() { }

    public Guid Id { get; private init; }
    public DateTimeOffset StartedAt { get; private init; }
    public IncidentState State { get; private set; }

    public IncidentNumber? IncidentNumber { get; private set; }
    public IlsNumber? IlsNumber { get; private set; }
    public string? Keyword { get; private set; }
    public string? Street { get; private set; }
    public string? District { get; private set; }
    public string? Status { get; private set; }

    public IReadOnlyList<ChecklistItem> Checklist => _checklist;
    public IReadOnlyList<EtbEntry> Journal => _journal;
    public IReadOnlyList<RoleAssignment> Roles => _roles;
    public IReadOnlyList<ForceUnit> Forces => _forces;
    public IReadOnlyList<AuditEvent> Audit => _audit;

    public static Incident Start(IClock clock, SessionOperator openedBy, string? keyword = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(openedBy);

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            StartedAt = clock.Now,
            State = IncidentState.Open,
            Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim()
        };
        incident._audit.Add(new AuditEvent(clock.Now, "opened", openedBy.Display));
        return incident;
    }

    public void SetIncidentNumber(IncidentNumber number) => IncidentNumber = number;
    public void SetIlsNumber(IlsNumber? number) => IlsNumber = number;
    public void SetKeyword(string? keyword) =>
        Keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
    public void SetAddress(string? street, string? district)
    {
        Street = string.IsNullOrWhiteSpace(street) ? null : street.Trim();
        District = string.IsNullOrWhiteSpace(district) ? null : district.Trim();
    }
    public void SetStatus(string? status) =>
        Status = string.IsNullOrWhiteSpace(status) ? null : status.Trim();
}
