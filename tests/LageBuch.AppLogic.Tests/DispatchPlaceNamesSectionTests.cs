using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class DispatchPlaceNamesSectionTests
{
    private readonly List<string> _questions = [];
    private int _changes;
    private Action? _confirm;

    private DispatchPlaceNamesSection Section(params DispatchPlaceName[] placeNames) =>
        new("Alarmierung", placeNames, () => _changes++, (question, remove) =>
        {
            _questions.Add(question);
            _confirm = remove;
        });

    [Fact]
    public void Rows_are_saved_trimmed_and_only_with_both_fields()
    {
        var section = Section();
        section.Rows.Add(new DispatchPlaceNameRow(" Fürstenfeldbruck ", " FFB ", () => { }));
        section.Rows.Add(new DispatchPlaceNameRow("Maisach", " ", () => { }));
        section.Rows.Add(new DispatchPlaceNameRow(" ", "OLC", () => { }));

        Assert.Equal([new DispatchPlaceName("Fürstenfeldbruck", "FFB")], section.ToValues());
    }

    [Fact]
    public void Adding_appends_a_blank_row_and_marks_the_editor_changed()
    {
        var section = Section(new DispatchPlaceName("Musterstadt", "MST"));
        object? added = null;
        section.RowAdded += (_, e) => added = e.Row;

        section.AddCommand.Execute(null);

        Assert.Equal(2, section.Rows.Count);
        Assert.Same(section.Rows[1], added);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void A_blank_row_just_added_goes_without_asking()
    {
        var section = Section();
        section.AddCommand.Execute(null);

        section.RemoveCommand.Execute(section.Rows[0]);

        Assert.Empty(section.Rows);
        Assert.Empty(_questions);
        Assert.Equal(2, _changes);
    }

    [Fact]
    public void A_stored_row_goes_only_once_the_question_is_answered()
    {
        var section = Section(new DispatchPlaceName("Musterstadt", "MST"));
        var row = section.Rows[0];

        section.RemoveCommand.Execute(row);

        Assert.Equal(["„Musterstadt“ aus Alarmierung entfernen?"], _questions);
        Assert.Single(section.Rows);
        Assert.Equal(0, _changes);

        Assert.NotNull(_confirm);
        _confirm();

        Assert.Empty(section.Rows);
        Assert.Equal(1, _changes);
    }

    [Fact]
    public void A_row_that_is_not_in_the_section_is_left_alone()
    {
        var section = Section(new DispatchPlaceName("Musterstadt", "MST"));

        section.RemoveCommand.Execute(new DispatchPlaceNameRow("Musterstadt", "MST", () => { }));

        Assert.Single(section.Rows);
        Assert.Empty(_questions);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void Typing_in_a_row_marks_the_editor_changed()
    {
        var section = Section(new DispatchPlaceName("Musterstadt", "MST"));

        section.Rows[0].Name = "Musterstadt Nord";
        section.Rows[0].Abbreviation = "MSN";

        Assert.Equal(2, _changes);
    }
}
