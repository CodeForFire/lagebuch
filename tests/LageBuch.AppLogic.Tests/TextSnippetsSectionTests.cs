using LageBuch.AppLogic.ViewModels;
using LageBuch.Domain.Tasks;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class TextSnippetsSectionTests
{
    private static TextSnippetsSection Section(params TextSnippet[] snippets) =>
        new("Textbausteine", snippets, () => { });

    [Fact]
    public void A_duplicate_kuerzel_is_named_until_it_is_resolved()
    {
        var section = Section(new TextSnippet("stl", "Erste"), new TextSnippet("abg", "Zweite"));
        Assert.Null(section.Conflicts);

        section.Rows[1].Shortcut = " STL ";
        Assert.Equal("Doppelte Kürzel: stl", section.Conflicts);

        section.Rows[1].Shortcut = "abg";
        Assert.Null(section.Conflicts);
    }

    [Fact]
    public void A_row_without_text_is_no_conflict_yet()
    {
        // The row being typed: + HINZUFÜGEN, then the Kürzel first. No complaint before the text.
        var section = Section(new TextSnippet("stl", "Erste"));
        section.AddCommand.Execute(null);
        section.Rows[1].Shortcut = "stl";

        Assert.Null(section.Conflicts);
    }

    [Fact]
    public void ToValues_saves_what_every_other_way_in_would_read()
    {
        var section = Section();
        section.AddCommand.Execute(null);
        section.Rows[0].Shortcut = "::LGB";
        section.Rows[0].Text = " Lage bestätigt: [X] ";
        section.Rows[0].Category = " Erkundung ";
        section.AddCommand.Execute(null); // left blank: dropped

        Assert.Equal(new TextSnippet("lgb", "Lage bestätigt: [X]", "Erkundung"), Assert.Single(section.ToValues()));
    }

    [Fact]
    public void Editing_any_field_marks_the_editor_dirty()
    {
        var changes = 0;
        var section = new TextSnippetsSection("Textbausteine", new[] { new TextSnippet("stl", "Erste") }, () => changes++);

        section.Rows[0].Text = "Geändert";
        section.Rows[0].Category = "Sonderfall";

        Assert.Equal(2, changes);
    }

    [Fact]
    public void Each_field_family_is_handed_only_its_own_list()
    {
        var etb = new[] { new TextSnippet("stl", "Stromleitung beschädigt") };
        var aufgaben = new[] { new TextSnippet("evu", "Energieversorger informieren") };
        var masterData = MasterDataSet.Empty with { TextSnippets = etb, TaskSnippets = aufgaben };
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 24, 9, 0, 0, TimeSpan.FromHours(2)));
        var session = TestSession.StartNew(
            new FakeStore(),
            clock,
            new Domain.SessionOperator("Müller"),
            "/x.fwincident",
            Array.Empty<(string, bool)>(),
            Array.Empty<(string, bool)>());

        var etbVm = new EtbViewModel(session, clock, masterData, () => { });
        using var tasksVm = new TasksViewModel(session, clock, new FakeTicker(), new FakeAlarmService(), masterData, () => { });
        var dialog = new TaskDialogViewModel(session, masterData, string.Empty, () => { });

        Assert.Same(etb, etbVm.TextSnippets);
        Assert.Same(aufgaben, tasksVm.TextSnippets);
        Assert.Same(aufgaben, dialog.TextSnippets);
    }

    [Fact]
    public void The_aufgaben_list_saves_its_priorities_and_the_etb_list_never_does()
    {
        var withLevels = new TextSnippet("rd", "RD nachfordern", Importance: TaskImportance.High, Urgency: TaskUrgency.High);
        var aufgaben = new TextSnippetsSection("Textbausteine Aufgaben", new[] { withLevels }, () => { }, withPriorities: true);
        var etb = new TextSnippetsSection("Textbausteine ETB", new[] { withLevels }, () => { });

        Assert.True(aufgaben.Rows[0].ShowsPriorities);
        Assert.False(etb.Rows[0].ShowsPriorities);
        Assert.Equal(withLevels, Assert.Single(aufgaben.ToValues()));
        Assert.Equal(new TextSnippet("rd", "RD nachfordern"), Assert.Single(etb.ToValues()));
    }
}
