using LageBuch.Domain.Leadership;

namespace LageBuch.AppLogic.Services;

/// <summary>A suggested Führungsstufe and the counts that decided it.</summary>
public sealed record FuehrungsstufeSuggestion(Fuehrungsstufe Stufe, string Reason);
