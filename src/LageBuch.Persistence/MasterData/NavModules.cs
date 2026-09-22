using LageBuch.Domain;

namespace LageBuch.Persistence.MasterData;

/// <summary>
/// The built-in modules of the Einsatz workspace, as stable string keys.
/// </summary>
/// <remarks>
/// Deliberately strings rather than an enum. An enum persisted as an integer is exactly the
/// mistake <c>ChecklistKind</c> made — the reason this work exists — because the numbers mean
/// whatever the declaration order happened to be when they were written. A key survives
/// reordering, and an unknown one from a newer build is skipped rather than silently read as a
/// different module.
/// </remarks>
public static class NavModules
{
    /// <summary>Einsatztagebuch. Always visible; see <c>NavigationLayout</c>.</summary>
    public const string Etb = "etb";

    /// <summary>Aufgaben.</summary>
    public const string Tasks = "tasks";

    /// <summary>Funktionen.</summary>
    public const string Roles = "roles";

    /// <summary>Kräfte.</summary>
    public const string Forces = "forces";

    /// <summary>Atemschutz.</summary>
    public const string Scba = "scba";

    /// <summary>CO-Messung.</summary>
    public const string Co = "co";

    /// <summary>Dateien.</summary>
    public const string Files = "files";

    /// <summary>Links.</summary>
    public const string Links = "links";

    /// <summary>Kontakte.</summary>
    public const string Contacts = "contacts";

    /// <summary>
    /// Not a module of its own: the marker for an entry that names one of the Einsatz's
    /// Checklisten through <see cref="NavEntry.ChecklistId"/>.
    /// </summary>
    public const string Checklist = "checklist";

    /// <summary>Every built-in module, in the order the rail shipped with.</summary>
    public static IReadOnlyList<string> All { get; } =
        new[] { Etb, Tasks, Roles, Forces, Scba, Co, Files, Links, Contacts };

    /// <summary>
    /// Whether this build knows the key. A layout written by a newer build may name a module that
    /// does not exist here yet; the resolver skips those rather than failing to open the Einsatz.
    /// </summary>
    public static bool IsKnown(string? key) =>
        key is not null
        && (string.Equals(key, Checklist, StringComparison.Ordinal)
            || All.Contains(key, StringComparer.Ordinal));
}

/// <summary>
/// One row of the Stammdaten <em>Navigation</em> list: a built-in module, or one Checkliste.
/// </summary>
/// <param name="ModuleKey">A <see cref="NavModules"/> key.</param>
/// <param name="ChecklistId">
/// The Checkliste this row names, when <paramref name="ModuleKey"/> is
/// <see cref="NavModules.Checklist"/>; null for a built-in module.
/// </param>
/// <param name="IsVisible">Whether the rail shows it. The ETB's flag is ignored — it is always shown.</param>
public sealed record NavEntry(string ModuleKey, Guid? ChecklistId, bool IsVisible)
{
    /// <summary>True when this row names a Checkliste rather than a built-in module.</summary>
    public bool IsChecklist => ChecklistId is not null;
}

/// <summary>The rail layout used when Stammdaten carry none.</summary>
public static class NavLayout
{
    /// <summary>
    /// The rail exactly as it shipped before it was configurable: Aufbau, the nine modules in
    /// their original order, then Abbau.
    /// </summary>
    /// <remarks>
    /// Stored as "no layout" rather than written into a fresh Stammdaten set on purpose. An empty
    /// layout means "whatever this build's default is", so a module added in a later release
    /// appears for everyone who never touched the Navigation list — where a materialized copy
    /// would have frozen them out of it.
    /// </remarks>
    public static IReadOnlyList<NavEntry> Default { get; } = new[]
    {
        new NavEntry(NavModules.Checklist, ChecklistDefaults.AufbauListId, true),
        new NavEntry(NavModules.Etb, null, true),
        new NavEntry(NavModules.Tasks, null, true),
        new NavEntry(NavModules.Roles, null, true),
        new NavEntry(NavModules.Forces, null, true),
        new NavEntry(NavModules.Scba, null, true),
        new NavEntry(NavModules.Co, null, true),
        new NavEntry(NavModules.Files, null, true),
        new NavEntry(NavModules.Links, null, true),
        new NavEntry(NavModules.Contacts, null, true),
        new NavEntry(NavModules.Checklist, ChecklistDefaults.AbbauListId, true),
    };
}
