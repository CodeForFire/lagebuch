using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain;
using LageBuch.Domain.Leadership;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

// The ETB's Einsatzabschnitte (#971): the strip that files a new entry, the edit panel's picker
// and the quick filter above the grid.
public class EtbSectionViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 22, 9, 0, 0, TimeSpan.FromHours(2));

    private static (LocalIncidentSession Session, EtbViewModel Vm, Guid Nord, Guid Sued) WithAbschnitte()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new SessionOperator("Müller", "ELW 1"),
            "/x.lagebuch",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord");
        session.AddCommandNode(CommandNodeKind.Bereitstellung, "Marktplatz");
        session.AddCommandNode(CommandNodeKind.Abschnitt, "Süd");
        var nord = session.Incident.CommandNodes.Single(n => n.Name == "Nord").Id;
        var sued = session.Incident.CommandNodes.Single(n => n.Name == "Süd").Id;
        return (session, new EtbViewModel(session, clock, MasterDataSet.Empty, () => { }), nord, sued);
    }

    private static EtbEntryRow Row(EtbViewModel vm, string text) => vm.Entries.Single(e => e.Text == text);

    private static void Add(EtbViewModel vm, string text, Guid? section = null)
    {
        vm.NewText = text;
        vm.NewSectionId = section;
        vm.AddEntryCommand.Execute(null);
    }

    [Fact]
    public void The_entry_strip_offers_the_live_abschnitte_but_no_bereitstellung()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        session.AddCommandNode(CommandNodeKind.Unterabschnitt, "Zugang", nord);

        Assert.True(vm.HasSectionChoices);
        Assert.Equal(["Nord", "Nord / Zugang", "Süd"], vm.SectionChoices.Select(c => c.Label));
        Assert.All(vm.SectionChoices, c => Assert.False(c.IsSelected));
    }

    [Fact]
    public void Without_abschnitte_the_strips_stay_hidden()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(), clock, new SessionOperator("Müller"), "/x.lagebuch", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });

        Assert.False(vm.HasSectionChoices);
        Assert.False(vm.HasFilterChoices);
    }

    [Fact]
    public void Choosing_an_abschnitt_files_the_new_entry_and_stays_chosen_for_the_next()
    {
        var (session, vm, nord, _) = WithAbschnitte();

        vm.SectionChoices.Single(c => c.Label == "Nord").SelectCommand.Execute(null);
        Assert.True(vm.SectionChoices.Single(c => c.Label == "Nord").IsSelected);
        vm.NewText = "Lage Nord";
        vm.AddEntryCommand.Execute(null);

        Assert.Equal(nord, session.Incident.Journal.Single(e => e.Text == "Lage Nord").SectionId);
        Assert.Equal(nord, vm.NewSectionId); // sticky: the next message usually comes from the same Abschnitt
        Assert.Equal("Nord", Row(vm, "Lage Nord").SectionName);
        Assert.True(Row(vm, "Lage Nord").HasSection);
    }

    [Fact]
    public void Choosing_the_chosen_abschnitt_again_files_the_next_entry_nowhere()
    {
        var (session, vm, _, _) = WithAbschnitte();
        var nord = vm.SectionChoices.Single(c => c.Label == "Nord");

        nord.SelectCommand.Execute(null);
        nord.SelectCommand.Execute(null);
        vm.NewText = "Lage allgemein";
        vm.AddEntryCommand.Execute(null);

        Assert.Null(vm.NewSectionId);
        Assert.False(nord.IsSelected);
        Assert.Null(session.Incident.Journal[^1].SectionId);
        Assert.False(Row(vm, "Lage allgemein").HasSection);
    }

    [Fact]
    public void A_dissolved_abschnitt_leaves_the_strip_and_the_choice()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        vm.NewSectionId = nord;

        session.DissolveCommandNode(nord);

        Assert.Equal(["Süd"], vm.SectionChoices.Select(c => c.Label));
        Assert.Null(vm.NewSectionId);
    }

    [Fact]
    public void The_quick_filter_shows_only_the_chosen_abschnitts_entries_and_alle_brings_them_back()
    {
        var (_, vm, nord, sued) = WithAbschnitte();
        Add(vm, "Lage Nord", nord);
        Add(vm, "Lage Süd", sued);
        Add(vm, "Lage allgemein");
        var all = vm.Entries.Count;

        Assert.Equal(["Alle", "Nord", "Süd"], vm.FilterChoices.Select(c => c.Label));
        Assert.True(vm.FilterChoices[0].IsSelected);

        vm.FilterChoices.Single(c => c.Label == "Nord").SelectCommand.Execute(null);

        Assert.Equal(nord, vm.SectionFilter);
        Assert.Equal(["Lage Nord"], vm.Entries.Select(e => e.Text));
        Assert.True(vm.FilterChoices.Single(c => c.Label == "Nord").IsSelected);
        Assert.False(vm.FilterChoices[0].IsSelected);

        vm.FilterChoices[0].SelectCommand.Execute(null);

        Assert.Null(vm.SectionFilter);
        Assert.Equal(all, vm.Entries.Count);
    }

    [Fact]
    public void The_quick_filter_combines_with_nur_lagemeldungen_and_the_system_filter()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        Add(vm, "Lage Nord", nord);
        session.AddSituationReport("ELW 1", "Blumenstraße 13", "Schuppen im Vollbrand", null, null, null);
        session.SendSituationReport(session.Incident.SituationReports[0].Id, "ILS");
        var report = session.Incident.Journal.Single(e => e.Text.StartsWith("1. Lagemeldung", StringComparison.Ordinal));
        session.AssignJournalEntrySection(report.Id, nord);

        vm.SectionFilter = nord;
        Assert.Equal(2, vm.Entries.Count);

        vm.ShowOnlySituationReports = true;
        Assert.Equal(report.Id, Assert.Single(vm.Entries).Id);

        // A System line is never filed under an Abschnitt, so showing them adds nothing here.
        vm.ShowOnlySituationReports = false;
        vm.HideSystemEntries = false;
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public void An_entry_filed_while_the_filter_is_on_appears_only_when_it_matches()
    {
        var (_, vm, nord, sued) = WithAbschnitte();
        vm.SectionFilter = nord;

        Add(vm, "Lage Süd", sued);
        Add(vm, "Lage Nord", nord);

        Assert.Equal(["Lage Nord"], vm.Entries.Select(e => e.Text));
    }

    [Fact]
    public void A_dissolved_abschnitt_with_entries_stays_in_the_filter_under_its_stored_name()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        Add(vm, "Lage Nord", nord);
        session.UpdateCommandNode(nord, "Nordost", null, null, null, null, null);
        session.DissolveCommandNode(nord);

        Assert.Equal(["Alle", "Süd", "Nord"], vm.FilterChoices.Select(c => c.Label));
        vm.FilterChoices.Single(c => c.Label == "Nord").SelectCommand.Execute(null);
        Assert.Equal(["Lage Nord"], vm.Entries.Select(e => e.Text));
        Assert.Equal("Nord", vm.Entries[0].SectionName);
    }

    [Fact]
    public void A_filter_on_an_abschnitt_that_leaves_the_list_falls_back_to_alle()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        vm.SectionFilter = nord;

        session.DissolveCommandNode(nord); // no entry was ever filed under it

        Assert.Null(vm.SectionFilter);
        Assert.True(vm.FilterChoices[0].IsSelected);
    }

    [Fact]
    public void The_edit_panel_picks_an_abschnitt_and_saves_it_with_the_text()
    {
        var (session, vm, nord, sued) = WithAbschnitte();
        Add(vm, "Lage", nord);
        var row = Row(vm, "Lage");

        row.BeginEditCommand.Execute(null);
        Assert.Equal("Nord", vm.EditSectionLabel);
        Assert.Equal([NodeChoices.UnplacedLabel, "Nord", "Süd"], vm.EditSectionChoices.Select(c => c.Label));
        Assert.True(vm.EditSectionChoices.Single(c => c.Label == "Nord").IsCurrent);

        vm.EditSectionChoices.Single(c => c.Label == "Süd").SelectCommand.Execute(null);
        Assert.Equal("Süd", vm.EditSectionLabel);
        Assert.Equal(nord, session.Incident.Journal.Single(e => e.Id == row.Id).SectionId); // nothing written before SPEICHERN

        vm.EditText = "Lage korrigiert";
        vm.SaveEditCommand.Execute(null);

        var saved = session.Incident.Journal.Single(e => e.Id == row.Id);
        Assert.Equal(sued, saved.SectionId);
        Assert.Equal("Lage korrigiert", saved.Text);
        Assert.Equal("Süd", row.SectionName); // the same row, updated in place
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void The_edit_panel_takes_an_entry_out_of_its_abschnitt_without_touching_the_text()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        Add(vm, "Lage", nord);
        var row = Row(vm, "Lage");
        vm.SectionFilter = nord;

        row.BeginEditCommand.Execute(null);
        vm.EditSectionChoices[0].SelectCommand.Execute(null);
        vm.SaveEditCommand.Execute(null);

        var saved = session.Incident.Journal.Single(e => e.Id == row.Id);
        Assert.Null(saved.SectionId);
        Assert.Empty(saved.Edits);
        Assert.False(row.HasSection);
        Assert.Empty(vm.Entries); // the filter on Nord no longer matches it
    }

    [Fact]
    public void Saving_without_a_change_of_abschnitt_writes_no_assignment()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        Add(vm, "Lage", nord);
        var before = session.Incident.Journal.Count;

        Row(vm, "Lage").BeginEditCommand.Execute(null);
        vm.SaveEditCommand.Execute(null);

        Assert.Equal(before, session.Incident.Journal.Count);
    }

    [Fact]
    public void The_edit_picker_still_shows_a_dissolved_abschnitt_the_entry_is_filed_under()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        Add(vm, "Lage", nord);
        session.DissolveCommandNode(nord);

        Row(vm, "Lage").BeginEditCommand.Execute(null);

        Assert.Equal("Nord", vm.EditSectionLabel);
        Assert.Equal([NodeChoices.UnplacedLabel, "Süd", "Nord"], vm.EditSectionChoices.Select(c => c.Label));
        vm.SaveEditCommand.Execute(null); // unchanged: no assignment to a dissolved Abschnitt is attempted
        Assert.Equal(nord, session.Incident.Journal.Single(e => e.Text == "Lage").SectionId);
    }

    [Fact]
    public void An_assignment_from_elsewhere_updates_the_row_and_the_filter()
    {
        var (session, vm, nord, sued) = WithAbschnitte();
        Add(vm, "Lage", nord);
        var row = Row(vm, "Lage");
        vm.SectionFilter = sued;
        Assert.Empty(vm.Entries);

        session.AssignJournalEntrySection(row.Id, sued);

        Assert.Same(row, Assert.Single(vm.Entries));
        Assert.Equal("Süd", row.SectionName);
    }

    [Fact]
    public void Forming_the_first_abschnitt_shows_both_strips()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(), clock, new SessionOperator("Müller"), "/x.lagebuch", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.AddCommandNode(CommandNodeKind.Abschnitt, "Nord");

        Assert.True(vm.HasSectionChoices);
        Assert.True(vm.HasFilterChoices);
        Assert.Contains(nameof(EtbViewModel.HasSectionChoices), raised);
        Assert.Contains(nameof(EtbViewModel.HasFilterChoices), raised);
    }

    [Fact]
    public void Picking_in_the_edit_panel_announces_the_new_label()
    {
        var (_, vm, _, _) = WithAbschnitte();
        Add(vm, "Lage");
        Row(vm, "Lage").BeginEditCommand.Execute(null);
        Assert.Equal(NodeChoices.UnplacedLabel, vm.EditSectionLabel);
        Assert.True(vm.HasEditSectionChoices);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.EditSectionChoices.Single(c => c.Label == "Nord").SelectCommand.Execute(null);

        Assert.Equal("Nord", vm.EditSectionLabel);
        Assert.Contains(nameof(EtbViewModel.EditSectionLabel), raised);
        Assert.Contains(nameof(EtbViewModel.HasEditSectionChoices), raised);
    }

    [Fact]
    public void Without_abschnitte_the_edit_panel_offers_no_picker()
    {
        var clock = new FixedClock(T0);
        var session = TestSession.StartNew(
            new FakeStore(), clock, new SessionOperator("Müller"), "/x.lagebuch", Array.Empty<(string, bool)>(), Array.Empty<(string, bool)>());
        var vm = new EtbViewModel(session, clock, MasterDataSet.Empty, () => { });
        Add(vm, "Lage");

        Row(vm, "Lage").BeginEditCommand.Execute(null);

        Assert.False(vm.HasEditSectionChoices);
    }

    [Fact]
    public void A_refiled_row_announces_its_abschnitt_and_a_text_edit_does_not()
    {
        var (session, vm, nord, _) = WithAbschnitte();
        Add(vm, "Lage");
        var row = Row(vm, "Lage");
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        session.EditJournalEntry(row.Id, "Lage korrigiert");
        Assert.DoesNotContain(nameof(EtbEntryRow.SectionName), raised);

        session.AssignJournalEntrySection(row.Id, nord);

        Assert.Contains(nameof(EtbEntryRow.SectionId), raised);
        Assert.Contains(nameof(EtbEntryRow.SectionName), raised);
        Assert.Contains(nameof(EtbEntryRow.HasSection), raised);
        Assert.True(row.HasSection);
    }
}
