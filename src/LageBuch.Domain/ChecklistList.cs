namespace LageBuch.Domain;

/// <summary>
/// One of an Einsatz's 0..n Checklisten: the incident's own copy of a Stammdaten template.
/// </summary>
/// <remarks>
/// The incident keeps the list's id <em>and</em> its title rather than looking either up in
/// Stammdaten at display time. That is what lets a PDF export, a joined sync device and a file
/// reopened months later name every list it contains — and what makes renaming or deleting a
/// template harmless to an Einsatz already under way.
/// <para>
/// A class, not a record: <see cref="ChecklistItem"/> is mutable and this is an aggregate child
/// of <see cref="Incident"/>, so value equality would be misleading.
/// </para>
/// </remarks>
public sealed class ChecklistList
{
    private readonly List<ChecklistItem> _items = new();

    private ChecklistList(Guid id, string title)
    {
        Id = id;
        Title = title;
    }

    /// <summary>The id of the Stammdaten template this list was seeded from.</summary>
    public Guid Id { get; }

    /// <summary>The list's name, as it was when the Einsatz started.</summary>
    public string Title { get; }

    /// <summary>The list's items, in order.</summary>
    public IReadOnlyList<ChecklistItem> Items => _items;

    /// <summary>
    /// True when every mandatory item is ticked. Vacuously true for a list with no mandatory
    /// items, which is how a purely optional checklist behaves today.
    /// </summary>
    public bool AllMandatoryDone => _items.Where(i => i.IsMandatory).All(i => i.IsDone);

    /// <summary>Restores a list read back out of an incident file or a sync snapshot.</summary>
    public static ChecklistList Rehydrate(Guid id, string title, IEnumerable<ChecklistItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = new ChecklistList(id, ChecklistDefaults.TitleOrFallback(title));
        list._items.AddRange(items);
        return list;
    }

    /// <summary>
    /// Instantiates a template at Einsatz start. The list keeps the template's id; every item gets
    /// a fresh one, so two Einsätze seeded from the same template never share item ids.
    /// </summary>
    internal static ChecklistList FromSeed(ChecklistSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        var list = new ChecklistList(seed.Id, ChecklistDefaults.TitleOrFallback(seed.Title));
        foreach (var (text, isMandatory) in seed.Items)
        {
            list._items.Add(new ChecklistItem(text, isMandatory));
        }

        return list;
    }

    internal ChecklistItem? Find(Guid itemId) => _items.FirstOrDefault(i => i.Id == itemId);
}
