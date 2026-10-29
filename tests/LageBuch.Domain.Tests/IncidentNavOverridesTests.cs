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

        Assert.Throws<ArgumentException>(() => incident.SetNavVisibility(new string('x', 65), true));
    }

    [Fact]
    public void Set_nav_visibility_rejects_a_65th_distinct_key()
    {
        var (incident, _, _) = NewIncident();
        for (var i = 0; i < 64; i++)
        {
            incident.SetNavVisibility($"k{i}", true);
        }

        Assert.Throws<ArgumentException>(() => incident.SetNavVisibility("k64", true));
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
        Assert.True(incident.NavOverrides[seed.Id.ToString("D")]);
        Assert.Equal(EtbDirection.System, incident.Journal[^1].Direction);
        Assert.Equal("Checkliste „Nachbereitung“ hinzugefügt", incident.Journal[^1].Text);
    }

    [Fact]
    public void Adding_a_checklist_already_in_the_einsatz_is_refused()
    {
        var (incident, clock, op) = NewIncident();
        var seed = Seed();
        incident.AddChecklist(clock, op, seed);

        Assert.Throws<InvalidOperationException>(() => incident.AddChecklist(clock, op, seed));
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

        Assert.Throws<ArgumentException>(() => incident.AddChecklist(clock, op, Seed(title: new string('t', 201))));
        Assert.Throws<ArgumentException>(() => incident.AddChecklist(clock, op, Seed(items: 201)));
        Assert.Throws<ArgumentException>(() => incident.AddChecklist(
            clock, op, new ChecklistSeed(Guid.NewGuid(), "Lang", [(new string('x', 1001), false)])));
        Assert.Empty(incident.Checklists);
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

        Assert.False(incident.NavOverrides["scba"]);
        Assert.DoesNotContain(string.Empty, incident.NavOverrides.Keys);
        Assert.DoesNotContain(new string('x', 65), incident.NavOverrides.Keys);
        Assert.True(incident.NavOverrides.Count <= 64);
    }

    [Fact]
    public void Rehydrate_without_overrides_has_none()
    {
        Assert.Empty(Rehydrated(null).NavOverrides);
    }
}
