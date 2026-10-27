namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A known Einsatznummer format offered in the Stammdaten (#462): the template that is filled in
/// when picked, and a label saying where it is used, shown beside it in the list.
/// </summary>
public sealed record IncidentNumberFormatSuggestion(string Template, string Label);
