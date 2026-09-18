namespace LageBuch.Domain;

/// <summary>
/// The two Checklisten every Lagebuch carried before Checklisten became Stammdaten.
/// </summary>
/// <remarks>
/// The ids are frozen and must never change. Three independent paths resolve to exactly these —
/// the incident file's V23 backfill of the old <c>kind</c> column, the Stammdaten store's one-time
/// move off <c>md_checklist_template</c>, and <c>MasterDataJson</c>'s legacy
/// <c>checklistTemplateAufbau</c>/<c>checklistTemplateAbbau</c> keys — so that a file, a Stammdaten
/// database and an exported JSON all keep agreeing about which list is which. Change one of these
/// and an Einsatz's Aufbau list stops being the Aufbau list the nav layout names.
/// <para>
/// Titles are stored bare ("Aufbau", not "Checkliste Aufbau"): the ETB completion entry
/// interpolates the title where it used to interpolate <c>ChecklistKind.ToString()</c>, so a
/// migrated file keeps producing byte-identical journal text.
/// </para>
/// </remarks>
public static class ChecklistDefaults
{
    /// <summary>Title given to a list whose own title is blank.</summary>
    public const string FallbackTitle = "Checkliste";

    /// <summary>Title of the seeded setup checklist.</summary>
    public const string AufbauTitle = "Aufbau";

    /// <summary>Title of the seeded teardown checklist.</summary>
    public const string AbbauTitle = "Abbau";

    /// <summary>Frozen id of the setup checklist. Never change.</summary>
    public static readonly Guid AufbauListId = new("6c2f1a44-0b7e-4d3a-9f21-8a5c1d0e7b10");

    /// <summary>Frozen id of the teardown checklist. Never change.</summary>
    public static readonly Guid AbbauListId = new("9d81e5b2-73c6-4f08-a1d4-2e6b93f5c8a7");

    /// <summary>
    /// A usable title for a list. A blank one is substituted, never rejected: a checklist whose
    /// name the operator cleared still holds their items, and dropping it would lose them.
    /// </summary>
    public static string TitleOrFallback(string? title) =>
        string.IsNullOrWhiteSpace(title) ? FallbackTitle : title.Trim();

    /// <summary>
    /// Wraps a legacy Aufbau/Abbau item pair as the two well-known lists.
    /// </summary>
    /// <remarks>
    /// Transitional, for the readers still handed the fixed pair — the incident repository before
    /// V23 gives a file its own <c>checklist_lists</c> table, and the sync snapshot before it
    /// carries n lists. Both lists are produced even when empty, so this stays faithful to the
    /// behaviour it is standing in for; the "drop an empty list" rule belongs to the migration and
    /// to Stammdaten parsing, not here. Delete this with its last caller.
    /// </remarks>
    public static IReadOnlyList<ChecklistList> AsLists(
        IEnumerable<ChecklistItem> aufbau,
        IEnumerable<ChecklistItem> abbau) =>
        new[]
        {
            ChecklistList.Rehydrate(AufbauListId, AufbauTitle, aufbau),
            ChecklistList.Rehydrate(AbbauListId, AbbauTitle, abbau),
        };
}
