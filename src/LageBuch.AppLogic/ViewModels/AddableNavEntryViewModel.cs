using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// One choice behind the rail's „+“: an entry of this Einsatz the rail does not show, or a
/// Checkliste from today's Stammdaten that the Einsatz does not hold yet.
/// </summary>
/// <remarks>
/// Exactly one of <paramref name="RailKey"/> and <paramref name="Template"/> is set. A
/// <paramref name="RailKey"/> is listed under „Ausgeblendet“ and brings that entry back; a
/// <paramref name="Template"/> is listed under „Checkliste hinzufügen“ and copies it into the
/// Einsatz (#337: the values, never a reference into the Stammdaten).
/// </remarks>
/// <param name="Header">The label, cased as the rail shows it.</param>
/// <param name="RailKey">The hidden entry's override key (<c>NavigationLayout.KeyOf</c>), or null.</param>
/// <param name="Template">The Stammdaten Vorlage to add, or null.</param>
public sealed record AddableNavEntryViewModel(string Header, string? RailKey, ChecklistTemplate? Template);
