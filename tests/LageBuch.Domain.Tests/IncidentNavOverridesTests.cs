using LageBuch.Domain.Etb;

namespace LageBuch.Domain.Tests;

public class IncidentNavOverridesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 30, 14, 0, 0, TimeSpan.FromHours(1));

    private static (Incident Incident, FixedClock Clock, SessionOperator Op) NewIncident()
    {
        var clock = new FixedClock(T0);
        var op = new SessionOperator("Muster", "FFB 12/1");
        return (Incident.Start(clock, op), clock, op);
    }

    private static ChecklistSeed Seed(Guid? id = null, string title = "Nachbereitung", int items = 1)
        => new(id ?? Guid.NewGuid(), title, Enumerable.Range(0, items).Select(i => ($"Punkt {i}", false)).ToList());

    private static Incident Rehydrated(IReadOnlyDictionary<string, bool>? overrides)
        => Incident.Rehydrate(
            Guid.NewGuid(),
            T0,
            IncidentState.Open,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<ChecklistList>(),
            Array.Empty<EtbEntry>(),
            Array.Empty<RoleAssignment>(),
            Array.Empty<ForceUnit>(),
            Array.Empty<Atemschutz.AtemschutzTrupp>(),
            Array.Empty<AuditEvent>(),
            Array.Empty<Time.IncidentTimerState>(),
            Array.Empty<Files.IncidentFile>(),
            Array.Empty<Tasks.IncidentTask>(),
            Array.Empty<CoMeasurement.Building>(),
            Array.Empty<CoMeasurement.Dwelling>(),
            Array.Empty<Involved.InvolvedParty>(),
            navOverrides: overrides);

    [Fact]
    public void Set_nav_visibility_records_the_override()
    {
        var (incident, _, _) = NewIncident();

        incident.SetNavVisibility("scba", false);
        Assert.False(incident.NavOverrides["scba"]);

        incident.SetNavVisibility("scba", true);
        Assert.True(incident.NavOverrides["scba"]);
    }

    [Fact]
    public void Set_nav_visibility_on_a_closed_einsatz_throws()
    {
        var (incident, clock, op) = NewIncident();
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.SetNavVisibility("scba", false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Set_nav_visibility_rejects_a_blank_key(string key)
    {
        var (incident, _, _) = NewIncident();

        Assert.Throws<ArgumentException>(() => incident.SetNavVisibility(key, true));
    }

    [Fact]
    public void Set_nav_visibility_rejects_an_overlong_key()
    {
        var (incident, _, _) = NewIncident();

        var ex = Assert.Throws<ArgumentException>(() => incident.SetNavVisibility(new string('x', 65), true));

        Assert.StartsWith("Navigationsschlüssel muss 1 bis 64 Zeichen lang sein.", ex.Message, StringComparison.Ordinal);
        Assert.Empty(incident.NavOverrides);
    }

    [Fact]
    public void Set_nav_visibility_accepts_a_key_of_exactly_the_maximum_length()
    {
        var (incident, _, _) = NewIncident();
        var key = new string('x', Incident.MaxNavKeyLength);

        incident.SetNavVisibility(key, false);

        Assert.False(incident.NavOverrides[key]);
    }

    [Fact]
    public void Set_nav_visibility_rejects_a_65th_distinct_key()
    {
        var (incident, _, _) = NewIncident();
        for (var i = 0; i < 64; i++)
        {
            incident.SetNavVisibility($"k{i}", true);
        }

        var ex = Assert.Throws<ArgumentException>(() => incident.SetNavVisibility("k64", true));

        Assert.StartsWith("Zu viele ausgeblendete Einträge.", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("k64", incident.NavOverrides.Keys);
        incident.SetNavVisibility("k0", false);
        Assert.False(incident.NavOverrides["k0"]);
        Assert.Equal(64, incident.NavOverrides.Count);
    }

    [Fact]
    public void Adding_a_checklist_appends_it_and_marks_it_visible()
    {
        var (incident, clock, op) = NewIncident();
        var seed = Seed();

        var list = incident.AddChecklist(clock, op, seed);

        var added = Assert.Single(incident.Checklists);
        Assert.Same(list, added);
        Assert.Equal(seed.Id, added.Id);
        Assert.Equal("Nachbereitung", added.Title);
        Assert.True(incident.NavOverrides[ChecklistList.NavKey(seed.Id)]);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
        Assert.Equal("Checkliste „Nachbereitung“ hinzugefügt", incident.Journal[^1].Text);
    }

    [Fact]
    public void Adding_a_checklist_with_a_null_title_or_item_text_is_refused_as_an_argument_error()
    {
        var (incident, clock, op) = NewIncident();
        var id = Guid.NewGuid();

        Assert.ThrowsAny<ArgumentException>(
            () => incident.AddChecklist(clock, op, new ChecklistSeed(id, null!, [("a", true)])));
        Assert.ThrowsAny<ArgumentException>(
            () => incident.AddChecklist(clock, op, new ChecklistSeed(id, "T", null!)));
        Assert.ThrowsAny<ArgumentException>(
            () => incident.AddChecklist(clock, op, new ChecklistSeed(id, "T", [(null!, true)])));
        Assert.Empty(incident.Checklists);
    }

    [Fact]
    public void Adding_a_checklist_with_one_null_item_text_among_valid_ones_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        var ex = Assert.Throws<ArgumentNullException>(() => incident.AddChecklist(
            clock, op, new ChecklistSeed(Guid.NewGuid(), "T", [("a", true), (null!, false), ("c", true)])));

        Assert.StartsWith("Ein Checklistenpunkt ohne Text ist nicht möglich.", ex.Message, StringComparison.Ordinal);
        Assert.Empty(incident.Checklists);
    }

    [Fact]
    public void Adding_a_checklist_already_in_the_einsatz_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var seed = Seed();
        incident.AddChecklist(clock, op, seed);

        var ex = Assert.Throws<InvalidOperationException>(() => incident.AddChecklist(clock, op, seed));

        Assert.Equal("Diese Checkliste ist bereits im Einsatz.", ex.Message);
        Assert.Single(incident.Checklists);
    }

    [Fact]
    public void Adding_a_checklist_to_a_closed_einsatz_throws()
    {
        var (incident, clock, op) = NewIncident();
        incident.Close(clock, op);

        Assert.Throws<IncidentClosedException>(() => incident.AddChecklist(clock, op, Seed()));
    }

    [Fact]
    public void Adding_a_checklist_over_the_caps_throws()
    {
        var (incident, clock, op) = NewIncident();
        var journal = incident.Journal.Count;

        Assert.Throws<ArgumentException>(() => incident.AddChecklist(clock, op, Seed(title: new string('t', 201))));
        Assert.Throws<ArgumentException>(() => incident.AddChecklist(clock, op, Seed(items: 201)));
        Assert.Throws<ArgumentException>(() => incident.AddChecklist(
            clock, op, new ChecklistSeed(Guid.NewGuid(), "Lang", [(new string('x', 1001), false)])));
        Assert.Empty(incident.Checklists);
        Assert.Empty(incident.NavOverrides);
        Assert.Equal(journal, incident.Journal.Count);
    }

    [Fact]
    public void A_checkliste_exactly_at_the_caps_is_accepted()
    {
        var (incident, clock, op) = NewIncident();
        var items = Enumerable.Range(0, ChecklistList.MaxItemCount)
            .Select(i => (i == 0 ? new string('x', ChecklistItem.MaxTextLength) : $"Punkt {i}", false))
            .ToList();

        incident.AddChecklist(clock, op, new ChecklistSeed(Guid.NewGuid(), new string('t', ChecklistList.MaxTitleLength), items));

        var list = Assert.Single(incident.Checklists);
        Assert.Equal(ChecklistList.MaxItemCount, list.Items.Count);
        Assert.Equal(ChecklistItem.MaxTextLength, list.Items[0].Text.Length);
    }

    [Fact]
    public void One_oversized_item_among_normal_ones_is_refused()
    {
        var (incident, clock, op) = NewIncident();

        var ex = Assert.Throws<ArgumentException>(() => incident.AddChecklist(
            clock,
            op,
            new ChecklistSeed(Guid.NewGuid(), "Lang", [("a", false), (new string('x', ChecklistItem.MaxTextLength + 1), false), ("c", true)])));

        Assert.StartsWith("Checkliste überschreitet die zulässige Größe.", ex.Message, StringComparison.Ordinal);
        Assert.Empty(incident.Checklists);
    }

    [Fact]
    public void Adding_a_checkliste_at_the_override_cap_throws_and_changes_nothing()
    {
        var (incident, clock, op) = NewIncident();
        for (var i = 0; i < Incident.MaxNavOverrides; i++)
        {
            incident.SetNavVisibility($"k{i}", true);
        }

        var journal = incident.Journal.Count;

        var ex = Assert.Throws<ArgumentException>(() => incident.AddChecklist(clock, op, Seed()));

        Assert.StartsWith("Zu viele ausgeblendete Einträge.", ex.Message, StringComparison.Ordinal);
        Assert.Empty(incident.Checklists);
        Assert.Equal(journal, incident.Journal.Count);
        Assert.Equal(Incident.MaxNavOverrides, incident.NavOverrides.Count);
    }

    [Fact]
    public void Adding_a_checkliste_whose_key_already_has_an_override_succeeds_at_the_cap()
    {
        var (incident, clock, op) = NewIncident();
        var seed = Seed();
        incident.SetNavVisibility(ChecklistList.NavKey(seed.Id), false);
        for (var i = 1; i < Incident.MaxNavOverrides; i++)
        {
            incident.SetNavVisibility($"k{i}", true);
        }

        incident.AddChecklist(clock, op, seed);

        Assert.Single(incident.Checklists);
        Assert.True(incident.NavOverrides[ChecklistList.NavKey(seed.Id)]);
        Assert.Equal(Incident.MaxNavOverrides, incident.NavOverrides.Count);
    }

    [Fact]
    public void Seeding_at_einsatz_start_is_not_capped()
    {
        var (incident, _, _) = NewIncident();

        incident.SeedChecklist([Seed(items: 250)]);

        Assert.Equal(250, incident.Checklists[0].Items.Count);
    }

    [Fact]
    public void Rehydrate_keeps_nav_overrides_and_drops_entries_over_the_caps()
    {
        var overrides = new Dictionary<string, bool> { ["scba"] = false, [string.Empty] = true, [new string('x', 65)] = true };
        for (var i = 0; i < 70; i++)
        {
            overrides[$"k{i}"] = true;
        }

        var incident = Rehydrated(overrides);

        // The invalid keys are skipped, and the valid ones are taken in order until the cap: scba
        // and k0..k62 make 64, so k63..k69 are dropped.
        Assert.False(incident.NavOverrides["scba"]);
        Assert.DoesNotContain(string.Empty, incident.NavOverrides.Keys);
        Assert.DoesNotContain(new string('x', 65), incident.NavOverrides.Keys);
        Assert.Equal(Incident.MaxNavOverrides, incident.NavOverrides.Count);
        Assert.Contains("k62", incident.NavOverrides.Keys);
        Assert.All(Enumerable.Range(63, 7), i => Assert.DoesNotContain($"k{i}", incident.NavOverrides.Keys));
    }

    [Fact]
    public void Rehydrate_without_overrides_has_none()
    {
        Assert.Empty(Rehydrated(null).NavOverrides);
    }
}
