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
                "checklist:Aufbau", "etb", "tasks", "leadership", "involved",
                "scba", "co", "files", "links", "contacts", "issued", "checklist:Abbau",
            },
            Keys(specs));
    }

    [Fact]
    public void A_layout_saved_before_beteiligte_existed_still_shows_it_after_fuehrung()
    {
        var saved = NavModules.All
            .Where(k => !string.Equals(k, NavModules.InvolvedParties, StringComparison.Ordinal))
            .Select(k => Module(k))
            .ToArray();

        var keys = Keys(NavigationLayout.Resolve(saved, Array.Empty<ChecklistList>()));

        Assert.Equal(keys.IndexOf(NavModules.Leadership) + 1, keys.IndexOf(NavModules.InvolvedParties));
    }

    // Funktionen and Kräfte were rail entries of their own until they became tabs of Führung. A
    // layout saved back then names all three, and the merged entry takes the first one's place --
    // which, unless someone reordered, is where Funktionen was.
    [Fact]
    public void A_layout_with_funktionen_and_kraefte_folds_them_into_fuehrung_at_the_first_ones_place()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module(NavModules.Tasks), Module("roles"), Module("forces"),
                Module(NavModules.InvolvedParties), Module(NavModules.Scba), Module(NavModules.Leadership),
                Module(NavModules.Co),
            },
            Array.Empty<ChecklistList>()));

        Assert.Equal(
            ["etb", "tasks", "leadership", "involved", "scba", "co", "files", "links", "contacts", "issued"],
            keys);
    }

    [Fact]
    public void A_layout_from_before_the_board_gets_fuehrung_where_its_first_entry_was()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module(NavModules.Scba), Module("forces"), Module("roles"),
                Module(NavModules.Files),
            },
            Array.Empty<ChecklistList>()));

        // CO-Messung, which this layout omits, is slotted in behind Atemschutz and so lands between.
        Assert.True(keys.IndexOf(NavModules.Scba) < keys.IndexOf(NavModules.Leadership));
        Assert.True(keys.IndexOf(NavModules.Leadership) < keys.IndexOf(NavModules.Files));
        Assert.True(keys.IndexOf(NavModules.Tasks) < keys.IndexOf(NavModules.Scba));
        Assert.Single(keys, k => k == NavModules.Leadership);
    }

    [Fact]
    public void Fuehrung_is_shown_when_any_of_the_entries_it_replaces_was()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module("roles", visible: false), Module("forces"),
                Module(NavModules.Leadership, visible: false),
            },
            Array.Empty<ChecklistList>()));

        Assert.Contains(NavModules.Leadership, keys);
    }

    [Fact]
    public void Fuehrung_stays_hidden_when_every_entry_it_replaces_was()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module("roles", visible: false), Module("forces", visible: false),
                Module(NavModules.Leadership, visible: false),
            },
            Array.Empty<ChecklistList>()));

        Assert.DoesNotContain(NavModules.Leadership, keys);
        Assert.DoesNotContain("roles", keys);
        Assert.DoesNotContain("forces", keys);
    }

    // Once the fold has been saved, the layout names only Führung. Moving it after Atemschutz is
    // then a deliberate choice, and nothing may pull it back to where Funktionen used to be.
    [Fact]
    public void A_layout_naming_only_fuehrung_keeps_it_where_it_was_put()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module(NavModules.Tasks), Module(NavModules.Scba),
                Module(NavModules.Leadership), Module(NavModules.InvolvedParties), Module(NavModules.Co),
                Module(NavModules.Files), Module(NavModules.Links), Module(NavModules.Contacts),
            },
            Array.Empty<ChecklistList>()));

        Assert.Equal(keys.IndexOf(NavModules.Scba) + 1, keys.IndexOf(NavModules.Leadership));
        Assert.Equal(keys.IndexOf(NavModules.Tasks) + 1, keys.IndexOf(NavModules.Scba));
    }

    [Fact]
    public void Folding_leaves_a_layout_without_funktionen_or_kraefte_untouched()
    {
        var layout = new[] { Module(NavModules.Etb), Module(NavModules.Leadership, visible: false) };

        Assert.Same(layout, NavigationLayout.FoldLegacyFuehrung(layout));
    }

    [Fact]
    public void Fuehrung_follows_aufgaben_to_wherever_the_layout_moved_it()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module(NavModules.Files), Module(NavModules.Tasks),
                Module(NavModules.Links),
            },
            Array.Empty<ChecklistList>()));

        Assert.Equal(keys.IndexOf(NavModules.Tasks) + 1, keys.IndexOf(NavModules.Leadership));
        Assert.True(keys.IndexOf(NavModules.Files) < keys.IndexOf(NavModules.Tasks));
    }

    // A switched-off Aufgaben still holds its place in the layout, so Führung does not jump
    // somewhere else just because a toggle is off.
    [Fact]
    public void Fuehrung_keeps_aufgabens_place_when_aufgaben_is_switched_off()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[]
            {
                Module(NavModules.Etb), Module(NavModules.Files), Module(NavModules.Tasks, visible: false),
                Module(NavModules.Links),
            },
            Array.Empty<ChecklistList>()));

        Assert.DoesNotContain(NavModules.Tasks, keys);
        Assert.Equal(keys.IndexOf(NavModules.Files) + 1, keys.IndexOf(NavModules.Leadership));
    }

    [Fact]
    public void A_layout_lacking_every_predecessor_appends_the_missing_module_at_the_end()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Module(NavModules.Co), Module(NavModules.Files) },
            Array.Empty<ChecklistList>()));

        // The ETB is the first default module, so it has no predecessor at all and is appended
        // after what the layout lists; the modules after it then find it as theirs.
        Assert.True(keys.IndexOf(NavModules.Etb) > keys.IndexOf(NavModules.Co));
        Assert.True(keys.IndexOf(NavModules.Etb) > keys.IndexOf(NavModules.Files));
        Assert.Equal(keys.IndexOf(NavModules.Etb) + 1, keys.IndexOf(NavModules.Tasks));
    }

    [Fact]
    public void Several_missing_modules_keep_their_default_relative_order()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Module(NavModules.Files) },
            Array.Empty<ChecklistList>()));

        Assert.Equal(
            new[]
            {
                "etb", "tasks", "leadership", "involved", "scba", "co", "files", "links",
                "contacts", "issued",
            },
            keys);
    }

    [Fact]
    public void A_module_switched_off_is_left_out()
    {
        var specs = NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Module(NavModules.Scba, visible: false), Module(NavModules.Files) },
            Array.Empty<ChecklistList>());

        Assert.Equal(
            ["etb", "tasks", "leadership", "involved", "co", "files", "links", "contacts", "issued"],
            Keys(specs));
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

        Assert.Equal(
            ["etb", "tasks", "leadership", "involved", "scba", "co", "files", "links", "contacts", "issued"],
            Keys(specs));
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

        Assert.Equal(
            ["etb", "tasks", "leadership", "involved", "scba", "co", "files", "links", "contacts", "issued"],
            Keys(specs));
    }

    // A layout saved by an older build cannot mention a module added since. Without this, that
    // module would be invisible forever to everyone who ever touched the Navigation list.
    [Fact]
    public void A_module_the_layout_predates_is_added_rather_than_lost()
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

        // The modules the layout predates slot in behind their predecessors, so they land between
        // the ETB and the list; the list keeps its place in front of Dateien.
        var keys = Keys(specs);
        Assert.Equal(keys.IndexOf("files") - 1, keys.IndexOf("checklist:Nachbereitung"));
        Assert.True(keys.IndexOf("etb") < keys.IndexOf("checklist:Nachbereitung"));
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

    private static Dictionary<string, bool> Overrides(params (string Key, bool Visible)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Visible, StringComparer.Ordinal);

    [Fact]
    public void A_hidden_override_removes_a_module_the_layout_shows()
    {
        var keys = Keys(NavigationLayout.Resolve(
            Array.Empty<NavEntry>(),
            Array.Empty<ChecklistList>(),
            Overrides((NavModules.Scba, false))));

        Assert.DoesNotContain(NavModules.Scba, keys);
    }

    [Fact]
    public void A_visible_override_shows_a_module_the_layout_switched_off_in_its_slot()
    {
        var plain = Keys(NavigationLayout.Resolve(Array.Empty<NavEntry>(), Array.Empty<ChecklistList>()));
        var layout = NavLayout.Default
            .Select(e => !e.IsChecklist && e.ModuleKey == NavModules.Scba ? Module(NavModules.Scba, false) : e)
            .ToArray();

        var keys = Keys(NavigationLayout.Resolve(
            layout, Array.Empty<ChecklistList>(), Overrides((NavModules.Scba, true))));

        Assert.Equal(plain, keys);
    }

    [Fact]
    public void A_hidden_override_removes_a_module_the_stored_layout_predates()
    {
        var layout = new[] { Module(NavModules.Etb), Module(NavModules.Files) };
        Assert.Contains(NavModules.Contacts, Keys(NavigationLayout.Resolve(layout, Array.Empty<ChecklistList>())));

        var keys = Keys(NavigationLayout.Resolve(
            layout, Array.Empty<ChecklistList>(), Overrides((NavModules.Contacts, false))));

        Assert.DoesNotContain(NavModules.Contacts, keys);
        Assert.Contains(NavModules.Files, keys);
    }

    [Fact]
    public void A_visible_override_shows_a_checkliste_the_layout_unticked()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb), Checklist(Aufbau, visible: false) },
            new[] { List(Aufbau, "Aufbau") },
            Overrides((ChecklistList.NavKey(Aufbau), true))));

        Assert.Contains("checklist:Aufbau", keys);
    }

    [Fact]
    public void A_hidden_override_removes_a_checkliste_the_layout_never_mentioned()
    {
        var keys = Keys(NavigationLayout.Resolve(
            new[] { Module(NavModules.Etb) },
            new[] { List(Nachbereitung, "Nachbereitung") },
            Overrides((ChecklistList.NavKey(Nachbereitung), false))));

        Assert.DoesNotContain("checklist:Nachbereitung", keys);
    }

    [Fact]
    public void The_etb_ignores_a_hidden_override()
    {
        var keys = Keys(NavigationLayout.Resolve(
            Array.Empty<NavEntry>(),
            Array.Empty<ChecklistList>(),
            Overrides((NavModules.Etb, false))));

        Assert.Contains(NavModules.Etb, keys);
    }

    [Fact]
    public void A_live_module_shows_despite_a_hidden_override()
    {
        var keys = Keys(NavigationLayout.Resolve(
            Array.Empty<NavEntry>(),
            Array.Empty<ChecklistList>(),
            Overrides((NavModules.Scba, false)),
            new HashSet<string>(StringComparer.Ordinal) { NavModules.Scba }));

        Assert.Contains(NavModules.Scba, keys);
    }

    [Fact]
    public void Unknown_override_keys_are_ignored()
    {
        var lists = new[] { List(Aufbau, "Aufbau") };
        var expected = Keys(NavigationLayout.Resolve(Array.Empty<NavEntry>(), lists));

        var keys = Keys(NavigationLayout.Resolve(
            Array.Empty<NavEntry>(),
            lists,
            Overrides(("nonsense", false), (ChecklistList.NavKey(Guid.NewGuid()), false))));

        Assert.Equal(expected, keys);
    }

    [Fact]
    public void A_hidden_override_applies_to_a_closed_einsatz()
    {
        var clock = new FixedClock(DateTimeOffset.UnixEpoch);
        var op = new SessionOperator("Host");
        var incident = Incident.Start(clock, op, "Brand");
        incident.SetNavVisibility(NavModules.Scba, false);
        incident.Close(clock, op);

        var keys = Keys(NavigationLayout.Resolve(
            Array.Empty<NavEntry>(), incident.Checklists, incident.NavOverrides));

        Assert.Equal(IncidentState.Closed, incident.State);
        Assert.DoesNotContain(NavModules.Scba, keys);
    }

    [Fact]
    public void KeyOf_is_the_module_key_or_the_checkliste_id()
    {
        Assert.Equal(NavModules.Scba, NavigationLayout.KeyOf(new NavItemSpec(NavModules.Scba, null)));
        Assert.Equal(
            ChecklistList.NavKey(Aufbau),
            NavigationLayout.KeyOf(new NavItemSpec(NavModules.Checklist, List(Aufbau, "Aufbau"))));
    }
}
