namespace LageBuch.Domain;

/// <summary>
/// One Stammdaten Checklisten-Vorlage as an Einsatz is started from it.
/// </summary>
/// <param name="Id">
/// The template's id, carried into the incident's own copy. It is the join key the nav rail
/// matches a Navigation layout entry against, so it must survive seeding unchanged.
/// </param>
/// <param name="Title">The list's name, bare — see <see cref="ChecklistDefaults"/>.</param>
/// <param name="Items">The items to create, in order.</param>
public sealed record ChecklistSeed(
    Guid Id,
    string Title,
    IReadOnlyList<(string Text, bool IsMandatory)> Items);
