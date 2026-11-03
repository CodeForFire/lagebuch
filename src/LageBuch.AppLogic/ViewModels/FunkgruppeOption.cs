namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// A Stammdaten Funkgruppe offered in a Funkgruppen picker: the text that is filled in when picked,
/// e.g. "FW_MUSTER (KW: 1000)", and the group's Notiz, shown beside it so the Lagebuchführer can see
/// what the group is for. <see cref="Note"/> is null when the Stammdaten give none.
/// </summary>
public sealed record FunkgruppeOption(string Text, string? Note);
