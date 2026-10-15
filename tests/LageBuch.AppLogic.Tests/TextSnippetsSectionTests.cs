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
}
