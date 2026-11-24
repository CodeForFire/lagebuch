using System.Collections.Specialized;
using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

/// <summary>The ETB search: narrows the journal to the entries containing every typed word.</summary>
public class EtbViewModelSearchTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void Search_keeps_only_entries_containing_every_term()
    {
        var (_, vm) = NewVm();
        Add(vm, "Atemschutz Trupp Nord vor");
        Add(vm, "Wasserversorgung Nord steht");
        Add(vm, "Atemschutz Trupp Süd zurück");

        vm.FilterText = "trupp nord atemschutz";

        var only = Assert.Single(vm.Entries);
        Assert.Equal("Atemschutz Trupp Nord vor", only.Text);
    }

    [Fact]
    public void Search_matches_von_an_and_erfasst_von_case_insensitively()
    {
        var (_, vm) = NewVm();
        Add(vm, "Lagemeldung", from: "ILS Fürstenfeldbruck");
        Add(vm, "Nachalarmierung", to: "Kreisbrandinspektion");
        Add(vm, "Wasserversorgung steht");

        vm.FilterText = "ils";
        Assert.Equal(["Lagemeldung"], vm.Entries.Select(r => r.Text));

        vm.FilterText = "KREISBRAND";
        Assert.Equal(["Nachalarmierung"], vm.Entries.Select(r => r.Text));

        // Erfasst von: the operator wrote all three.
        vm.FilterText = "müller";
        Assert.Equal(3, vm.Entries.Count);
    }

    [Fact]
    public void Search_combines_with_hide_system_entries()
    {
        var (_, vm) = NewVm();
        Add(vm, "Einsatz läuft");

        // "Einsatz begonnen" is a System entry and contains the term too.
        vm.FilterText = "einsatz";
        Assert.Equal(["Einsatz läuft"], vm.Entries.Select(r => r.Text));

        vm.HideSystemEntries = false;
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void Entry_arriving_during_a_search_shows_only_if_it_matches()
    {
        var (_, vm) = NewVm();
        vm.FilterText = "drehleiter";

        Add(vm, "Wasserversorgung steht");
        Assert.Empty(vm.Entries);

        Add(vm, "Drehleiter in Stellung");
        Assert.Equal(["Drehleiter in Stellung"], vm.Entries.Select(r => r.Text));
    }

    [Fact]
    public void Editing_an_entry_so_it_no_longer_matches_hides_it()
    {
        var (session, vm) = NewVm();
        Add(vm, "Drehleiter in Stellung");
        vm.FilterText = "drehleiter";
        var id = Assert.Single(vm.Entries).Id;

        session.EditJournalEntry(id, "Hubrettungsfahrzeug in Stellung");

        Assert.Empty(vm.Entries);

        session.EditJournalEntry(id, "Drehleiter wieder in Stellung");

        Assert.Equal(["Drehleiter wieder in Stellung"], vm.Entries.Select(r => r.Text));
    }

    [Fact]
    public void Clearing_the_search_restores_every_entry_in_place()
    {
        // A Reset takes the grid's selected row and focus with it (#542).
        var (_, vm) = NewVm();
        Add(vm, "Erste");
        Add(vm, "Zweite");
        var before = vm.Entries.ToList();
        var resets = 0;
        vm.Entries.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                resets++;
            }
        };

        vm.FilterText = "zweite";
        Assert.Single(vm.Entries);
        Assert.True(vm.IsFiltered);

        vm.ClearFilterCommand.Execute(null);

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.False(vm.IsFiltered);
        Assert.Equal(before, vm.Entries);
        Assert.Equal(0, resets);
    }

    [Fact]
    public void Match_count_reads_visible_of_every_shown_entry()
    {
        var (_, vm) = NewVm();
        Add(vm, "Atemschutz Trupp 2 vor");
        Add(vm, "Wasserversorgung steht");
        Add(vm, "Atemschutz Trupp 1 zurück");

        vm.FilterText = "atemschutz";
        Assert.Equal("2 von 3", vm.MatchCountText);

        Add(vm, "Atemschutz Trupp 3 bereit");
        Assert.Equal("3 von 4", vm.MatchCountText);
    }

    [Fact]
    public void Showing_systemmeldungen_during_a_search_updates_the_count_even_when_no_row_moves()
    {
        var (_, vm) = NewVm();
        Add(vm, "Wasserversorgung steht");
        vm.FilterText = "wasser";
        Assert.Equal("1 von 1", vm.MatchCountText);
        var raised = 0;
        vm.PropertyChanged += (_, e) => raised += e.PropertyName == nameof(EtbViewModel.MatchCountText) ? 1 : 0;

        // "Einsatz begonnen" does not match, so the list stays as it is; the whole grows by one.
        vm.HideSystemEntries = false;

        Assert.Single(vm.Entries);
        Assert.Equal("1 von 2", vm.MatchCountText);
        Assert.True(raised > 0);
    }

    [Fact]
    public void Search_matches_the_abschnitt_an_entry_is_filed_under()
    {
        var (session, created) = NewVm();
        using var vm = created;
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nordflügel");
        vm.NewSectionId = session.Incident.CommandNodes.Single(n => n.Name == "Nordflügel").Id;
        Add(vm, "Lage erkundet");
        vm.NewSectionId = null;
        Add(vm, "Wasserversorgung steht");

        vm.FilterText = "nordflügel";

        Assert.Equal(["Lage erkundet"], vm.Entries.Select(r => r.Text));
    }

    [Fact]
    public void Search_narrows_the_abschnitt_filter_and_counts_against_it()
    {
        var (session, created) = NewVm();
        using var vm = created;
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord");
        var nord = session.Incident.CommandNodes.Single(n => n.Name == "Nord").Id;
        vm.NewSectionId = nord;
        Add(vm, "Atemschutz Trupp vor");
        Add(vm, "Wasserversorgung steht");
        vm.NewSectionId = null;
        Add(vm, "Atemschutz Trupp zurück");
        vm.SectionFilter = nord;

        vm.FilterText = "atemschutz";

        Assert.Equal(["Atemschutz Trupp vor"], vm.Entries.Select(r => r.Text));
        Assert.Equal("1 von 2", vm.MatchCountText);
    }

    [Fact]
    public void No_matches_message_quotes_the_trimmed_search()
    {
        var (_, vm) = NewVm();
        Add(vm, "Wasserversorgung steht");

        vm.FilterText = "  Drehleiter ";

        Assert.Empty(vm.Entries);
        Assert.True(vm.ShowNoMatches);
        Assert.Equal("Keine Einträge passen zu „Drehleiter“.", vm.NoMatchesMessage);

        vm.FilterText = "wasser";
        Assert.False(vm.ShowNoMatches);
    }

    private static void Add(EtbViewModel vm, string text, string? from = null, string? to = null)
    {
        vm.NewText = text;
        vm.NewFrom = from;
        vm.NewTo = to;
        vm.AddEntryCommand.Execute(null);
    }

    private static (LocalIncidentSession Session, EtbViewModel Vm) NewVm()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "FFB 12/1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        return (session, new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }));
    }
}
