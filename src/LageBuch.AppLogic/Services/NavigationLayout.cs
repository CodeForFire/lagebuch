using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Services;

/// <summary>
/// Turns the Stammdaten Navigation layout plus one Einsatz's own Checklisten into the rail.
/// </summary>
/// <remarks>
/// The layout is global and live; an Einsatz carries its own copy of the Checklisten it was
/// started with. The two can therefore always disagree — a list the layout names may not be in
/// this file, and a file may hold a list saved long before the layout knew about it — and this is
/// where that is reconciled. A pure function: no state, no IO, so the rules can be tested on their
/// own, which matters because every one of them is a way an operator could lose sight of a list
/// they filled in.
/// </remarks>
public static class NavigationLayout
{
    /// <summary>
    /// Resolves the rail, in order.
    /// </summary>
    /// <param name="layout">The Stammdaten layout. Empty means <see cref="NavLayout.Default"/>.</param>
    /// <param name="incidentLists">This Einsatz's Checklisten, in its own order.</param>
    public static IReadOnlyList<NavItemSpec> Resolve(
        IReadOnlyList<NavEntry> layout,
        IReadOnlyList<ChecklistList> incidentLists)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(incidentLists);

        var effective = layout.Count == 0 ? NavLayout.Default : layout;

        // Hidden modules keep a slot (Spec null) so a module the layout predates can still be placed
        // after its predecessor when that predecessor was switched off.
        var slots = new List<Slot>(effective.Count);
        var seenLists = new HashSet<Guid>();
        var seenModules = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in effective)
        {
            if (entry.IsChecklist)
            {
                // Marked seen whether or not it is shown: "switched off" is not "unmentioned", and
                // the append pass below must not resurrect a list the operator deliberately
                // unticked.
                if (!seenLists.Add(entry.ChecklistId!.Value))
                {
                    continue;
                }

                var list = incidentLists.FirstOrDefault(l => l.Id == entry.ChecklistId!.Value);
                if (list is not null && entry.IsVisible)
                {
                    slots.Add(new Slot(NavModules.Checklist, new NavItemSpec(NavModules.Checklist, list)));
                }

                continue;
            }

            // A layout written by a newer build may name a module that does not exist here.
            if (!NavModules.IsKnown(entry.ModuleKey) || !seenModules.Add(entry.ModuleKey))
            {
                continue;
            }

            // The ETB is the legal record and where every system entry lands, so its flag is
            // ignored rather than trusted -- the editor disables the toggle, but a hand-edited
            // JSON can still say false.
            slots.Add(new Slot(
                entry.ModuleKey,
                entry.IsVisible || IsEtb(entry.ModuleKey) ? new NavItemSpec(entry.ModuleKey, null) : null));
        }

        // Anything this Einsatz holds that the layout never mentioned goes at the end, in the
        // incident's own order. That is what makes an archived Einsatz show all of its lists --
        // including one created after it was closed, or deleted from Stammdaten since.
        foreach (var list in incidentLists.Where(l => !seenLists.Contains(l.Id)))
        {
            slots.Add(new Slot(NavModules.Checklist, new NavItemSpec(NavModules.Checklist, list)));
        }

        // Same idea for a module this build has but the stored layout predates: without it, a
        // module added in a later release would stay invisible to everyone who ever saved a
        // layout. It lands after its predecessor in the default rail, not at the end.
        InsertMissingModules(
            slots, s => s.Key, k => new Slot(k, new NavItemSpec(k, null)), seenModules);

        var result = slots.Where(s => s.Spec is not null).Select(s => s.Spec!).ToList();

        // Unreachable while NavModules.All contains the ETB -- the pass above already appends it
        // when a layout omits it. Kept as the one place the "the ETB is always in the rail"
        // guarantee is stated outright, so removing it from All cannot quietly drop it.
        if (!result.Any(s => IsEtb(s.ModuleKey)))
        {
            result.Insert(0, new NavItemSpec(NavModules.Etb, null));
        }

        return result;
    }

    /// <summary>
    /// Inserts every built-in module <paramref name="seen"/> lacks after the nearest preceding
    /// module of <see cref="NavLayout.Default"/> that <paramref name="items"/> holds — switched-off
    /// ones included, so the position does not depend on a toggle — and appends it when none is
    /// there. Several missing modules keep their default relative order. Shared with the Stammdaten
    /// editor's Navigation list, so editor and rail cannot disagree.
    /// </summary>
    internal static void InsertMissingModules<T>(
        List<T> items,
        Func<T, string?> moduleKeyOf,
        Func<string, T> create,
        IReadOnlySet<string> seen)
    {
        var defaults = NavLayout.Default
            .Where(e => !e.IsChecklist)
            .Select(e => e.ModuleKey)
            .Concat(NavModules.All)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < defaults.Count; i++)
        {
            var key = defaults[i];
            if (seen.Contains(key))
            {
                continue;
            }

            var at = items.Count;
            for (var p = i - 1; p >= 0; p--)
            {
                var predecessor = defaults[p];
                var index = items.FindIndex(
                    item => string.Equals(moduleKeyOf(item), predecessor, StringComparison.Ordinal));
                if (index >= 0)
                {
                    at = index + 1;
                    break;
                }
            }

            items.Insert(at, create(key));
        }
    }

    private sealed record Slot(string Key, NavItemSpec? Spec);

    private static bool IsEtb(string key) => string.Equals(key, NavModules.Etb, StringComparison.Ordinal);
}

/// <summary>One rail entry, resolved: a built-in module, or one of this Einsatz's Checklisten.</summary>
/// <param name="ModuleKey">A <see cref="NavModules"/> key.</param>
/// <param name="List">The Checkliste this entry shows, or null for a built-in module.</param>
public sealed record NavItemSpec(string ModuleKey, ChecklistList? List);
