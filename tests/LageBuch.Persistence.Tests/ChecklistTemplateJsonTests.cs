using System.Text;
using LageBuch.Domain;
using LageBuch.Persistence.MasterData;

namespace LageBuch.Persistence.Tests;

// Stammdaten now hold 0..n named Checkliste templates and a Navigation layout, where they used to
// hold a fixed Aufbau/Abbau pair. Every Stammdaten file in the wild predates that, so the two
// older shapes have to keep importing -- onto the frozen ids, or an imported file's Aufbau list
// stops being the one the layout names.
public class ChecklistTemplateJsonTests
{
    private static MasterDataSet Parse(string json) =>
        MasterDataJson.Parse(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static MasterDataSet RoundTrip(MasterDataSet set) => MasterDataJson.Parse(MasterDataJson.Serialize(set));

    [Fact]
    public void The_new_checklists_array_is_read_with_ids_titles_and_items()
    {
        var set = Parse("""
            {
              "checklists": [
                { "id": "6c2f1a44-0b7e-4d3a-9f21-8a5c1d0e7b10", "title": "Aufbau",
                  "items": [{ "text": "Schritt 1", "mandatory": true }] },
                { "id": "33333333-3333-3333-3333-333333333333", "title": "Nachbereitung",
                  "items": [{ "text": "Bericht", "mandatory": false }, { "text": "Ablage", "mandatory": false }] }
              ]
            }
            """);

        Assert.Equal(2, set.ChecklistTemplates.Count);
        Assert.Equal(ChecklistDefaults.AufbauListId, set.ChecklistTemplates[0].Id);
        Assert.Equal("Nachbereitung", set.ChecklistTemplates[1].Title);
        Assert.Equal(2, set.ChecklistTemplates[1].Items.Count);
        Assert.True(set.ChecklistTemplates[0].Items[0].IsMandatory);
    }

    [Fact]
    public void The_legacy_aufbau_abbau_keys_import_onto_the_frozen_ids()
    {
        var set = Parse("""
            {
              "checklistTemplateAufbau": [{ "text": "Schritt 1", "mandatory": true }],
              "checklistTemplateAbbau": [{ "text": "Abbauschritt", "mandatory": false }]
            }
            """);

        Assert.Equal(
            new[] { ChecklistDefaults.AufbauListId, ChecklistDefaults.AbbauListId },
            set.ChecklistTemplates.Select(t => t.Id));
        Assert.Equal(new[] { "Aufbau", "Abbau" }, set.ChecklistTemplates.Select(t => t.Title));
    }

    // Same rule the V23 file migration follows: no items, no list.
    [Fact]
    public void A_legacy_key_with_no_items_produces_no_template()
    {
        var set = Parse("""{ "checklistTemplateAufbau": [{ "text": "Schritt 1", "mandatory": true }] }""");

        Assert.Equal(ChecklistDefaults.AufbauListId, Assert.Single(set.ChecklistTemplates).Id);
    }

    [Fact]
    public void The_oldest_flat_checklistTemplate_array_becomes_one_optional_aufbau_list()
    {
        var set = Parse("""{ "checklistTemplate": ["Schritt 1", "Schritt 2"] }""");

        var template = Assert.Single(set.ChecklistTemplates);
        Assert.Equal(ChecklistDefaults.AufbauListId, template.Id);
        Assert.Equal("Aufbau", template.Title);
        Assert.Equal(new[] { "Schritt 1", "Schritt 2" }, template.Items.Select(i => i.Text));
        Assert.All(template.Items, i => Assert.False(i.IsMandatory));
    }

    [Fact]
    public void The_new_array_wins_over_both_legacy_shapes()
    {
        var set = Parse("""
            {
              "checklistTemplate": ["Ignoriert"],
              "checklistTemplateAufbau": [{ "text": "Auch ignoriert", "mandatory": true }],
              "checklists": [{ "id": "33333333-3333-3333-3333-333333333333", "title": "Neu",
                               "items": [{ "text": "Schritt", "mandatory": false }] }]
            }
            """);

        Assert.Equal("Neu", Assert.Single(set.ChecklistTemplates).Title);
    }

    [Fact]
    public void A_file_with_no_checklist_key_at_all_has_no_templates()
    {
        Assert.Empty(Parse("""{ "roles": ["EL"] }""").ChecklistTemplates);
    }

    [Fact]
    public void A_template_with_a_blank_title_falls_back_rather_than_losing_its_items()
    {
        var set = Parse("""
            { "checklists": [{ "id": "33333333-3333-3333-3333-333333333333", "title": "  ",
                               "items": [{ "text": "Schritt", "mandatory": false }] }] }
            """);

        var template = Assert.Single(set.ChecklistTemplates);
        Assert.Equal(ChecklistDefaults.FallbackTitle, template.Title);
        Assert.Single(template.Items);
    }

    [Fact]
    public void Checklists_round_trip_through_serialize()
    {
        var set = MasterDataSet.Empty with
        {
            ChecklistTemplates = new[]
            {
                new ChecklistTemplate(
                    ChecklistDefaults.AufbauListId,
                    "Aufbau",
                    new[] { new ChecklistTemplateItem("Schritt 1", true) }),
                new ChecklistTemplate(
                    new Guid("33333333-3333-3333-3333-333333333333"),
                    "Nachbereitung",
                    new[] { new ChecklistTemplateItem("Bericht", false) }),
            },
        };

        var back = RoundTrip(set);

        Assert.Equal(
            set.ChecklistTemplates.Select(t => (t.Id, t.Title, t.Items.Count)),
            back.ChecklistTemplates.Select(t => (t.Id, t.Title, t.Items.Count)));
    }

    // --- Navigation ---
    [Fact]
    public void Navigation_is_read_with_module_keys_checklist_ids_and_visibility()
    {
        var set = Parse("""
            {
              "navigation": [
                { "module": "checklist", "checklistId": "6c2f1a44-0b7e-4d3a-9f21-8a5c1d0e7b10", "visible": true },
                { "module": "etb", "visible": true },
                { "module": "scba", "visible": false }
              ]
            }
            """);

        Assert.Equal(3, set.Navigation.Count);
        Assert.Equal(ChecklistDefaults.AufbauListId, set.Navigation[0].ChecklistId);
        Assert.True(set.Navigation[0].IsChecklist);
        Assert.Equal(NavModules.Etb, set.Navigation[1].ModuleKey);
        Assert.Null(set.Navigation[1].ChecklistId);
        Assert.False(set.Navigation[2].IsVisible);
    }

    // A hand-written file that lists modules means "show these"; absent means visible.
    [Fact]
    public void A_navigation_entry_without_visible_is_visible()
    {
        var set = Parse("""{ "navigation": [{ "module": "etb" }] }""");

        Assert.True(Assert.Single(set.Navigation).IsVisible);
    }

    // Empty means "use the default"; it is resolved at display time, never materialized here,
    // so that a later release adding a module is not permanently hidden by a stored layout.
    [Fact]
    public void A_file_with_no_navigation_key_has_an_empty_layout()
    {
        Assert.Empty(Parse("""{ "roles": ["EL"] }""").Navigation);
    }

    [Fact]
    public void Navigation_round_trips_through_serialize()
    {
        var set = MasterDataSet.Empty with
        {
            Navigation = new[]
            {
                new NavEntry(NavModules.Checklist, ChecklistDefaults.AbbauListId, true),
                new NavEntry(NavModules.Etb, null, true),
                new NavEntry(NavModules.Co, null, false),
            },
        };

        Assert.Equal(set.Navigation, RoundTrip(set).Navigation);
    }

    // Navigation must not count towards IsEmpty: the editor offers Import only while the set is
    // empty, so one saved layout would otherwise suppress that bootstrap on a fresh install --
    // the same trap the Settings doc-comment already warns about.
    [Fact]
    public void A_set_holding_only_a_navigation_layout_is_still_empty()
    {
        var set = MasterDataSet.Empty with
        {
            Navigation = new[] { new NavEntry(NavModules.Etb, null, true) },
        };

        Assert.True(set.IsEmpty);
    }

    [Fact]
    public void A_set_holding_a_checklist_template_is_not_empty()
    {
        var set = MasterDataSet.Empty with
        {
            ChecklistTemplates = new[]
            {
                new ChecklistTemplate(ChecklistDefaults.AufbauListId, "Aufbau", Array.Empty<ChecklistTemplateItem>()),
            },
        };

        Assert.False(set.IsEmpty);
    }
}
