using LageBuch.AppLogic.Services;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// The Navigation layout is global and live, but an Einsatz carries its own Checklisten -- so the
// two can always disagree: a list the layout names may not be in this file, and a file may hold a
// list saved before the layout knew about it. These rules are where that is reconciled, and every
// one of them is a way an operator could lose sight of a list they filled in.
public class NavigationLayoutTests
{
    private static readonly Guid Aufbau = ChecklistDefaults.AufbauListId;
    private static readonly Guid Abbau = ChecklistDefaults.AbbauListId;
    private static readonly Guid Nachbereitung = new("33333333-3333-3333-3333-333333333333");

    private static ChecklistList List(Guid id, string title) =>
        ChecklistList.Rehydrate(id, title, new[] { ChecklistItem.Rehydrate(Guid.NewGuid(), "x", false, null, false) });

    private static NavEntry Module(string key, bool visible = true) => new(key, null, visible);

    private static NavEntry Checklist(Guid id, bool visible = true) => new(NavModules.Checklist, id, visible);

    private static List<string> Keys(IEnumerable<NavItemSpec> specs) =>
        specs.Select(s => s.List is { } l ? $"checklist:{l.Title}" : s.ModuleKey).ToList();

    [Fact]
    public void An_empty_layout_reproduces_the_rail_as_it_shipped()
    {
        var specs = NavigationLayout.Resolve(
            Array.Empty<NavEntry>(),
            new[] { List(Aufbau, "Aufbau"), List(Abbau, "Abbau") });

        Assert.Equal(
            new[]
            {
                "checklist:Aufbau", "etb", "tasks", "roles", "forces",
                "scba", "co", "files", "links", "checklist:Abbau",
            },
            Keys(specs));
    }

    [Fact]
    public void A_module_switched_off_is_left_out()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Module(NavModules.Scba, visible: false), Module(NavModules.Files) },
            Array.Empty<ChecklistList>());

        Assert.Equal(new[] { "etb", "files" }, Keys(specs).Take(2));
        Assert.DoesNotContain("scba", Keys(specs));
    }

    // The ETB is the legal record and the target of every system entry; hiding it would make the
    // app lie about where those went. The editor disables its checkbox, but a hand-edited JSON
    // can still say false, so the resolver ignores the flag rather than trusting the UI.
    [Fact]
    public void The_etb_is_shown_even_when_the_layout_says_hidden()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb, visible: false), Module(NavModules.Files) },
            Array.Empty<ChecklistList>());

        Assert.Equal(new[] { "etb", "files" }, Keys(specs).Take(2));
    }

    // Omitting an entry is not how a module is hidden -- `visible: false` is. Omission only
    // happens in a hand-edited file or a layout written before that module existed, and in both
    // cases appending is the safe reading. So the ETB survives omission too.
    [Fact]
    public void The_etb_is_present_even_when_the_layout_omits_it_entirely()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Files), Module(NavModules.Links) },
            Array.Empty<ChecklistList>());

        Assert.Contains(NavModules.Etb, Keys(specs));
    }

    [Fact]
    public void Omitting_a_module_does_not_hide_it_only_visible_false_does()
    {
        var omitted = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb) },
            Array.Empty<ChecklistList>());
        var switchedOff = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Module(NavModules.Scba, visible: false) },
            Array.Empty<ChecklistList>());

        Assert.Contains(NavModules.Scba, Keys(omitted));
        Assert.DoesNotContain(NavModules.Scba, Keys(switchedOff));
    }

    [Fact]
    public void A_checklist_the_layout_names_but_this_einsatz_lacks_is_skipped()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Checklist(Aufbau), Module(NavModules.Etb), Checklist(Nachbereitung) },
            new[] { List(Aufbau, "Aufbau") }));

        Assert.Equal("checklist:Aufbau", Assert.Single(keys, k => k.StartsWith("checklist:", StringComparison.Ordinal)));
    }

    // An archived Einsatz must always show every list it actually holds, even one created after
    // it was closed, or deleted from Stammdaten since.
    [Fact]
    public void A_checklist_the_layout_never_mentions_is_appended_after_the_ones_it_does()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Checklist(Aufbau), Module(NavModules.Etb) },
            new[] { List(Aufbau, "Aufbau"), List(Nachbereitung, "Nachbereitung") }));

        Assert.Contains("checklist:Nachbereitung", keys);
        Assert.True(keys.IndexOf("checklist:Aufbau") < keys.IndexOf("checklist:Nachbereitung"));
    }

    [Fact]
    public void Appended_checklists_keep_the_incidents_own_order()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb) },
            new[] { List(Abbau, "Abbau"), List(Nachbereitung, "Nachbereitung"), List(Aufbau, "Aufbau") });

        Assert.Equal(
            new[] { "checklist:Abbau", "checklist:Nachbereitung", "checklist:Aufbau" },
            Keys(specs).Where(k => k.StartsWith("checklist:", StringComparison.Ordinal)));
    }

    // The distinction that makes the append rule safe: "not mentioned" is not the same as
    // "switched off". Resurrecting a list the operator deliberately unticked would make the
    // toggle useless.
    [Fact]
    public void A_checklist_switched_off_is_not_resurrected_by_the_append_rule()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Checklist(Nachbereitung, visible: false) },
            new[] { List(Nachbereitung, "Nachbereitung") }));

        Assert.DoesNotContain("checklist:Nachbereitung", keys);
        Assert.Contains(NavModules.Etb, keys);
    }

    [Fact]
    public void A_module_key_this_build_does_not_know_is_skipped()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Module("zeitschiene"), Module(NavModules.Files) },
            Array.Empty<ChecklistList>());

        Assert.Equal(new[] { "etb", "files" }, Keys(specs).Take(2));
        Assert.DoesNotContain("zeitschiene", Keys(specs));
    }

    // A layout saved by an older build cannot mention a module added since. Without this, that
    // module would be invisible forever to everyone who ever touched the Navigation list.
    [Fact]
    public void A_module_the_layout_predates_is_appended_rather_than_lost()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Module(NavModules.Files) },
            Array.Empty<ChecklistList>());

        Assert.Equal(NavModules.All.Count, Keys(specs).Count);
        Assert.Contains(NavModules.Co, Keys(specs));
        Assert.Contains(NavModules.Scba, Keys(specs));
    }

    [Fact]
    public void An_einsatz_without_checklists_simply_has_none_in_the_rail()
    {
        var specs = NavigationLayout.Resolve(Array.Empty<NavEntry>(), Array.Empty<ChecklistList>());

        Assert.Equal(NavModules.All, Keys(specs));
    }

    [Fact]
    public void A_layout_may_place_a_checklist_anywhere()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Checklist(Nachbereitung), Module(NavModules.Files) },
            new[] { List(Nachbereitung, "Nachbereitung") });

        Assert.Equal(new[] { "etb", "checklist:Nachbereitung", "files" }, Keys(specs).Take(3));
    }

    [Fact]
    public void The_same_checklist_listed_twice_appears_once()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Checklist(Aufbau), Module(NavModules.Etb), Checklist(Aufbau) },
            new[] { List(Aufbau, "Aufbau") });

        Assert.Single(Keys(specs), k => k == "checklist:Aufbau");
    }

    [Fact]
    public void Resolve_carries_the_incidents_list_object_through()
    {
        var aufbau = List(Aufbau, "Aufbau");

        var spec = Assert.Single(
            NavigationLayout.Resolve(new[] { Checklist(Aufbau) }, new[] { aufbau }),
            s => s.List is not null);

        Assert.Same(aufbau, spec.List);
        Assert.Equal(NavModules.Checklist, spec.ModuleKey);
    }
}
