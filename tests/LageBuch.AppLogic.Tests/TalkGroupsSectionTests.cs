using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class TalkGroupsSectionTests
{
    [Fact]
    public void Adding_a_row_marks_the_editor_changed_and_announces_the_row()
    {
        var changed = 0;
        var groups = new TalkGroupsSection("Funkgruppen", Array.Empty<TalkGroup>(), () => changed++);
        object? announced = null;
        groups.RowAdded += (_, e) => announced = e.Row;

        groups.AddCommand.Execute(null);

        Assert.Equal(1, changed);
        Assert.Same(groups.Rows[0], announced);
    }

    [Fact]
    public void A_blank_row_just_added_goes_without_asking_and_marks_the_editor_changed()
    {
        var changed = 0;
        var asked = 0;
        var groups = new TalkGroupsSection("Funkgruppen", Array.Empty<TalkGroup>(), () => changed++, (_, _) => asked++);
        groups.AddCommand.Execute(null);

        groups.RemoveCommand.Execute(groups.Rows[0]);

        Assert.Empty(groups.Rows);
        Assert.Equal(0, asked);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void A_stored_row_asks_first_and_goes_only_once_confirmed()
    {
        var changed = 0;
        string? question = null;
        Action? confirm = null;
        var groups = new TalkGroupsSection(
            "Funkgruppen",
            new[] { new TalkGroup("FW_MUSTER", "1000", TalkGroupKind.Tmo) },
            () => changed++,
            (q, run) =>
            {
                question = q;
                confirm = run;
            });

        groups.RemoveCommand.Execute(groups.Rows[0]);

        Assert.Single(groups.Rows);
        Assert.Equal("„FW_MUSTER“ aus Funkgruppen entfernen?", question);
        Assert.Equal(0, changed);

        Assert.NotNull(confirm);
        confirm();

        Assert.Empty(groups.Rows);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Removing_a_row_that_is_not_in_the_section_does_nothing()
    {
        var changed = 0;
        var asked = 0;
        var other = new TalkGroupsSection("Andere", new[] { new TalkGroup("X", null, TalkGroupKind.Tmo) }, () => { });
        var groups = new TalkGroupsSection(
            "Funkgruppen",
            new[] { new TalkGroup("FW_MUSTER", null, TalkGroupKind.Tmo) },
            () => changed++,
            (_, _) => asked++);

        groups.RemoveCommand.Execute(other.Rows[0]);

        Assert.Single(groups.Rows);
        Assert.Equal(0, asked);
        Assert.Equal(0, changed);
    }
}
