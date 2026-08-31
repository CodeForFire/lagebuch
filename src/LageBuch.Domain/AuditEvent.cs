namespace LageBuch.Domain;

public sealed record AuditEvent(DateTimeOffset At, string Action, string By);
