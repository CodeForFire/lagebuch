namespace LageBuch.AppLogic.Services;

/// <summary>A Führungsspanne hint; <paramref name="NodeId"/> null is a hint for the Einsatzleitung.</summary>
public sealed record SpanHint(Guid? NodeId, string Text);
