using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

// An Einsatz used to carry exactly two checklists, fixed by a ChecklistKind enum. It now carries
// 0..n, each with its own id and title, seeded from the Stammdaten templates. These pin the shape
// that makes that possible -- above all that a seeded list keeps the template's id, which is the
// join key the nav rail matches a layout entry against.
public class ChecklistListTests
{
    private static readonly DateTimeOffset T0 =
        new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static readonly Guid Aufbau = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Abbau = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Nachbereitung = new("33333333-3333-3333-3333-333333333333");

    private static Incident NewIncident(out FixedClock clock, out SessionOperator op)
    {
        clock = new FixedClock(T0);
        op = new SessionOperator("Müller", "FFB 12/1");
        return Incident.Start(clock, op);
    }

    private static ChecklistSeed Seed(Guid id, string title, params (string Text, bool IsMandatory)[] items) =>
        new(id, title, items);

    [Fact]
    public void An_incident_starts_with_no_checklists()
    {
        var incident = NewIncident(out _, out _);

        Assert.Empty(incident.Checklists);
    }

    [Fact]
    public void Seeding_no_lists_leaves_the_incident_without_checklists()
    {
        var incident = NewIncident(out _, out _);

        incident.SeedChecklist(Array.Empty<ChecklistSeed>());

        Assert.Empty(incident.Checklists);
    }

    [Fact]
    public void Seeded_lists_keep_their_order_titles_and_items()
    {
        var incident = NewIncident(out _, out _);

        incident.SeedChecklist(new[]
        {
            Seed(Aufbau, "Aufbau", ("Fahrzeug prüfen", true)),
            Seed(Abbau, "Abbau", ("Standort räumen", true), ("Material zählen", false)),
            Seed(Nachbereitung, "Nachbereitung", ("Bericht schreiben", false)),
        });

        Assert.Equal(
            new[] { "Aufbau", "Abbau", "Nachbereitung" },
            incident.Checklists.Select(l => l.Title));
        Assert.Equal("Fahrzeug prüfen", Assert.Single(incident.Checklists[0].Items).Text);
        Assert.Equal(2, incident.Checklists[1].Items.Count);
        Assert.True(incident.Checklists[1].Items[0].IsMandatory);
        Assert.False(incident.Checklists[1].Items[1].IsMandatory);
    }

    // The nav layout names a checklist by the template's Guid, so a seeded list has to carry that
    // same Guid or the rail could never match a layout entry to the list in the file.
    [Fact]
    public void A_seeded_list_keeps_the_template_id_it_came_from()
    {
        var incident = NewIncident(out _, out _);

        incident.SeedChecklist(new[] { Seed(Nachbereitung, "Nachbereitung", ("Bericht schreiben", false)) });

        Assert.Equal(Nachbereitung, Assert.Single(incident.Checklists).Id);
    }

    // Item ids stay per-incident: two Einsätze seeded from one template must not share item ids.
    [Fact]
    public void Item_ids_are_fresh_per_incident_even_though_list_ids_are_not()
    {
        var first = NewIncident(out _, out _);
        var second = NewIncident(out _, out _);
        var seed = new[] { Seed(Aufbau, "Aufbau", ("Fahrzeug prüfen", true)) };

        first.SeedChecklist(seed);
        second.SeedChecklist(seed);

        Assert.Equal(first.Checklists[0].Id, second.Checklists[0].Id);
        Assert.NotEqual(first.Checklists[0].Items[0].Id, second.Checklists[0].Items[0].Id);
    }

    [Fact]
    public void Toggling_resolves_an_item_on_any_list_not_just_the_first_two()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(new[]
        {
            Seed(Aufbau, "Aufbau", ("Fahrzeug prüfen", true)),
            Seed(Abbau, "Abbau", ("Standort räumen", true)),
            Seed(Nachbereitung, "Nachbereitung", ("Bericht schreiben", false)),
        });
        var third = incident.Checklists[2].Items[0];

        Assert.True(incident.ToggleChecklistItem(clock, op, third.Id).IsDone);
        Assert.True(incident.Checklists[2].Items[0].IsDone);
        Assert.False(incident.Checklists[0].Items[0].IsDone);
    }

    [Fact]
    public void Toggle_of_an_unknown_item_still_throws()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(new[] { Seed(Aufbau, "Aufbau", ("Fahrzeug prüfen", true)) });

        Assert.Throws<KeyNotFoundException>(
            () => incident.ToggleChecklistItem(clock, op, Guid.NewGuid()));
    }

    // The ETB line used to be interpolated from the enum, so it read "Checkliste Aufbau ...".
    // It now comes from the list's own title -- which is why titles are stored bare ("Aufbau",
    // not "Checkliste Aufbau"): a migrated file keeps producing byte-identical text.
    [Fact]
    public void Completing_a_lists_mandatory_items_names_that_list_in_the_etb_entry()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(new[]
        {
            Seed(Aufbau, "Aufbau", ("Fahrzeug prüfen", true)),
            Seed(Nachbereitung, "Nachbereitung", ("Bericht schreiben", true)),
        });
        var before = incident.Journal.Count;

        incident.ToggleChecklistItem(clock, op, incident.Checklists[1].Items[0].Id);

        Assert.Equal(before + 1, incident.Journal.Count);
        var entry = incident.Journal[^1];
        Assert.Equal(EtbDirection.System, entry.Direction);
        Assert.Equal("Checkliste Nachbereitung abgeschlossen: alle Pflichtpunkte erledigt", entry.Text);
    }

    // Completing one list says nothing about any other list's mandatory items.
    [Fact]
    public void Completing_one_list_does_not_log_for_another()
    {
        var incident = NewIncident(out var clock, out var op);
        incident.SeedChecklist(new[]
        {
            Seed(Aufbau, "Aufbau", ("Fahrzeug prüfen", true)),
            Seed(Abbau, "Abbau", ("Standort räumen", true)),
        });
        var before = incident.Journal.Count;

        incident.ToggleChecklistItem(clock, op, incident.Checklists[0].Items[0].Id);

        Assert.Equal(before + 1, incident.Journal.Count);
        Assert.Contains("Aufbau", incident.Journal[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Rehydrated_lists_keep_their_ids_titles_and_order()
    {
        var restored = ChecklistList.Rehydrate(
            Nachbereitung,
            "Nachbereitung",
            new[] { ChecklistItem.Rehydrate(Guid.NewGuid(), "Bericht schreiben", true, "erledigt", false) });

        Assert.Equal(Nachbereitung, restored.Id);
        Assert.Equal("Nachbereitung", restored.Title);
        var item = Assert.Single(restored.Items);
        Assert.True(item.IsDone);
        Assert.Equal("erledigt", item.Note);
    }
}
