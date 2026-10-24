using LageBuch.AppLogic.ViewModels;
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
    public void A_duplicate_kuerzel_already_in_the_stammdaten_is_named_on_opening()
    {
        // Only a store edited outside the app can hold one, but the editor still names it.
        var section = Section(new TextSnippet("stl", "Erste"), new TextSnippet("stl", "Zweite"));

        Assert.Equal("Doppelte Kürzel: stl", section.Conflicts);
    }

    [Fact]
    public void Rows_without_a_kuerzel_are_no_conflict()
    {
        var section = Section(new TextSnippet("stl", "Erste"), new TextSnippet("abg", "Zweite"));
        section.Rows[0].Shortcut = string.Empty;
        section.Rows[1].Shortcut = "--";

        Assert.Null(section.Conflicts);
    }

    [Fact]
    public void The_kategorie_suggestions_are_the_ones_in_use_each_once()
    {
        var section = Section(
            new TextSnippet("stl", "Erste", "Sonderfall"),
            new TextSnippet("abg", "Zweite"),
            new TextSnippet("lgb", "Dritte", "sonderfall"),
            new TextSnippet("lga", "Vierte", "Erkundung"));

        Assert.Equal(new[] { "Sonderfall", "Erkundung" }, section.Rows[0].CategoryOptions);
    }

    [Fact]
    public void Adding_a_row_marks_the_editor_dirty_and_announces_it()
    {
        var changes = 0;
        var section = new TextSnippetsSection("Textbausteine", Array.Empty<TextSnippet>(), () => changes++);
        object? announced = null;
        section.RowAdded += (_, e) => announced = e.Row;

        section.AddCommand.Execute(null);

        Assert.Equal(1, changes);
        Assert.Same(section.Rows[0], announced);
    }

    [Fact]
    public void Removing_a_row_asks_first_and_marks_the_editor_dirty()
    {
        var changes = 0;
        string? asked = null;
        var section = new TextSnippetsSection(
            "Textbausteine",
            new[] { new TextSnippet("stl", "Erste"), new TextSnippet("abg", "Zweite") },
            () => changes++,
            (name, remove) =>
            {
                asked = name;
                remove();
            });

        section.RemoveCommand.Execute(section.Rows[0]);

        Assert.Equal("„stl“ aus Textbausteine entfernen?", asked);
        Assert.Equal("abg", Assert.Single(section.Rows).Shortcut);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Removing_a_row_that_is_already_gone_does_nothing()
    {
        var changes = 0;
        var asked = false;
        var section = new TextSnippetsSection(
            "Textbausteine",
            new[] { new TextSnippet("stl", "Erste") },
            () => changes++,
            (_, remove) =>
            {
                asked = true;
                remove();
            });
        var row = section.Rows[0];
        section.RemoveCommand.Execute(row);
        changes = 0;
        asked = false;

        section.RemoveCommand.Execute(row);

        Assert.False(asked);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Moving_a_row_changes_the_order_the_suggestions_list_them_in()
    {
        var changes = 0;
        var section = new TextSnippetsSection(
            "Textbausteine",
            new[] { new TextSnippet("a", "Eins"), new TextSnippet("b", "Zwei"), new TextSnippet("c", "Drei") },
            () => changes++);

        section.MoveUpCommand.Execute(section.Rows[1]);
        Assert.Equal(new[] { "b", "a", "c" }, section.ToValues().Select(s => s.Shortcut));

        section.MoveDownCommand.Execute(section.Rows[1]);
        Assert.Equal(new[] { "b", "c", "a" }, section.ToValues().Select(s => s.Shortcut));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Moving_past_either_end_does_nothing()
    {
        var changes = 0;
        var section = new TextSnippetsSection(
            "Textbausteine",
            new[] { new TextSnippet("a", "Eins"), new TextSnippet("b", "Zwei") },
            () => changes++);

        section.MoveUpCommand.Execute(section.Rows[0]);
        section.MoveDownCommand.Execute(section.Rows[1]);
        section.MoveDownCommand.Execute(new TextSnippetRow("x", "Fremd", string.Empty, Array.Empty<string>(), () => { }));

        Assert.Equal(new[] { "a", "b" }, section.ToValues().Select(s => s.Shortcut));
        Assert.Equal(0, changes);
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
    public void A_kuerzel_is_unique_across_the_areas()
    {
        // One list: ::evu cannot report in the ETB and give out an Aufgabe under the same Kürzel.
        var section = Section(
            new TextSnippet("evu", "EVU vor Ort", ForEtb: true, ForTasks: false),
            new TextSnippet("evx", "Energieversorger informieren", ForEtb: false, ForTasks: true));

        section.Rows[1].Shortcut = "evu";

        Assert.Equal("Doppelte Kürzel: evu", section.Conflicts);
    }

    [Fact]
    public void A_new_textbaustein_is_offered_in_every_area()
    {
        var section = Section();
        section.AddCommand.Execute(null);

        Assert.True(section.Rows[0].ForEtb);
        Assert.True(section.Rows[0].ForTasks);
    }

    [Fact]
    public void ToValues_keeps_each_rows_areas()
    {
        var section = Section(new TextSnippet("stl", "Stromleitung beschädigt"), new TextSnippet("evu", "Energieversorger informieren"));
        section.Rows[0].ForTasks = false;
        section.Rows[1].ForEtb = false;
        section.Rows[1].ForTasks = false;

        Assert.Equal(
            new[]
            {
                new TextSnippet("stl", "Stromleitung beschädigt", ForEtb: true, ForTasks: false),
                new TextSnippet("evu", "Energieversorger informieren", ForEtb: false, ForTasks: false),
            },
            section.ToValues());
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
        section.Rows[0].ForEtb = false;
        section.Rows[0].ForTasks = false;

        Assert.Equal(4, changes);
    }

    [Fact]
    public void Each_field_is_offered_only_the_textbausteine_ticked_for_its_area()
    {
        var etbOnly = new TextSnippet("stl", "Stromleitung beschädigt", ForEtb: true, ForTasks: false);
        var tasksOnly = new TextSnippet("evu", "Energieversorger informieren", ForEtb: false, ForTasks: true);
        var both = new TextSnippet("pol", "Polizei verständigen");
        var neither = new TextSnippet("alt", "Abgelegt", ForEtb: false, ForTasks: false);
        var masterData = MasterDataSet.Empty with { TextSnippets = new[] { etbOnly, tasksOnly, both, neither } };
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

        Assert.Equal(new[] { etbOnly, both }, etbVm.TextSnippets);
        Assert.Equal(new[] { tasksOnly, both }, tasksVm.TextSnippets);
        Assert.Equal(new[] { tasksOnly, both }, dialog.TextSnippets);
    }
}
