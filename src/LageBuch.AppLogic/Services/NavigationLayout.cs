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
        var result = new List<NavItemSpec>(effective.Count);
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
                    result.Add(new NavItemSpec(NavModules.Checklist, list));
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
            if (entry.IsVisible || IsEtb(entry.ModuleKey))
            {
                result.Add(new NavItemSpec(entry.ModuleKey, null));
            }
        }

        // Anything this Einsatz holds that the layout never mentioned goes at the end, in the
        // incident's own order. That is what makes an archived Einsatz show all of its lists --
        // including one created after it was closed, or deleted from Stammdaten since.
        foreach (var list in incidentLists.Where(l => !seenLists.Contains(l.Id)))
        {
            result.Add(new NavItemSpec(NavModules.Checklist, list));
        }

        // Same idea for a module this build has but the stored layout predates: without it, a
        // module added in a later release would stay invisible to everyone who ever saved a
        // layout.
        foreach (var key in NavModules.All.Where(k => !seenModules.Contains(k)))
        {
            result.Add(new NavItemSpec(key, null));
        }

        // Unreachable while NavModules.All contains the ETB -- the pass above already appends it
        // when a layout omits it. Kept as the one place the "the ETB is always in the rail"
        // guarantee is stated outright, so removing it from All cannot quietly drop it.
        if (!result.Any(s => IsEtb(s.ModuleKey)))
        {
            result.Insert(0, new NavItemSpec(NavModules.Etb, null));
        }

        return result;
    }

    private static bool IsEtb(string key) => string.Equals(key, NavModules.Etb, StringComparison.Ordinal);
}

/// <summary>One rail entry, resolved: a built-in module, or one of this Einsatz's Checklisten.</summary>
/// <param name="ModuleKey">A <see cref="NavModules"/> key.</param>
/// <param name="List">The Checkliste this entry shows, or null for a built-in module.</param>
public sealed record NavItemSpec(string ModuleKey, ChecklistList? List);
