using LageBuch.AppLogic.ViewModels;
using LageBuch.Persistence.MasterData;

namespace LageBuch.AppLogic.Tests;

public class PremisesSectionTests
{
    private static readonly Premises Lindenallee = AnonymizedExampleData.Premises[0];

    private static PremisesSection Section(params Premises[] premises) =>
        new("Objekte", premises, () => { });

    [Fact]
    public void An_unchanged_section_saves_what_it_was_given()
    {
        var section = Section(AnonymizedExampleData.Premises.ToArray());

        Assert.Equal(AnonymizedExampleData.Premises, section.ToValues());
    }

    [Fact]
    public void A_row_without_a_name_and_a_second_row_of_the_same_name_are_not_saved()
    {
        var section = Section(new Premises("Haus A", "Hauptstraße"), new Premises("  "), new Premises("haus a"));

        Assert.Equal(new[] { new Premises("Haus A", "Hauptstraße") }, section.ToValues());
    }

    [Fact]
    public void The_label_grid_is_built_only_while_open_top_floor_first()
    {
        var section = Section(Lindenallee);
        var row = section.Rows[0];
        Assert.Empty(row.Floors);

        row.ToggleLabelGridCommand.Execute(null);

        Assert.Equal(new[] { "4. OG", "3. OG", "2. OG", "1. OG", "EG", "1. UG" }, row.Floors.Select(f => f.FloorLabel));
        Assert.Equal(new[] { "Hausmeister", string.Empty, "Kinderwagen" }, row.Floors[4].Cells.Select(c => c.Label));
        Assert.Equal(new[] { "Links", "Mitte", "Rechts" }, row.Floors[4].Cells.Select(c => c.Placeholder));

        row.ToggleLabelGridCommand.Execute(null);

        Assert.Empty(row.Floors);
    }

    [Fact]
    public void Typing_a_label_saves_it_and_marks_the_editor_dirty()
    {
        var changes = 0;
        var section = new PremisesSection("Objekte", new[] { new Premises("Haus A", FloorCount: 1, ApartmentsPerFloor: 2, UndergroundFloorCount: 0) }, () => changes++);
        var row = section.Rows[0];
        row.ToggleLabelGridCommand.Execute(null);

        row.Floors[0].Cells[1].Label = "Dachwohnung";

        Assert.Equal(1, changes);
        Assert.Equal("Dachwohnung", section.ToValues()[0].ApartmentLabels["1:2"]);
        Assert.Equal("WOHNUNGEN SCHLIESSEN", row.LabelToggleText);
        row.ToggleLabelGridCommand.Execute(null);
        Assert.Equal("WOHNUNGEN BENENNEN (1)", row.LabelToggleText);
    }

    // Taking a floor away and putting it back is an ordinary slip of the spinner: what was typed
    // for that floor comes back with it, and only what fits the structure on SPEICHERN is saved.
    [Fact]
    public void A_label_survives_its_floor_being_taken_away_and_put_back()
    {
        var section = Section(Lindenallee);
        var row = section.Rows[0];
        row.ToggleLabelGridCommand.Execute(null);

        row.UndergroundFloorCount = 0;
        Assert.DoesNotContain(section.ToValues()[0].ApartmentLabels.Keys, k => k.StartsWith("-1:", StringComparison.Ordinal));
        Assert.Equal(5, row.Floors.Count);

        row.UndergroundFloorCount = 1;
        Assert.Equal("Heizung", section.ToValues()[0].ApartmentLabels["-1:1"]);
        Assert.Equal("Heizung", row.Floors[5].Cells[0].Label);
    }

    [Fact]
    public void Clearing_a_label_removes_it()
    {
        var section = Section(Lindenallee);
        var row = section.Rows[0];
        row.ToggleLabelGridCommand.Execute(null);

        row.Floors[4].Cells[0].Label = "  ";

        Assert.False(section.ToValues()[0].ApartmentLabels.ContainsKey("0:1"));
        Assert.Equal(3, row.LabelCount);
    }

    [Fact]
    public void Adding_a_row_starts_from_the_dialog_defaults_and_announces_it()
    {
        var changes = 0;
        var section = new PremisesSection("Objekte", Array.Empty<Premises>(), () => changes++);
        object? announced = null;
        section.RowAdded += (_, e) => announced = e.Row;

        section.AddCommand.Execute(null);

        var row = Assert.Single(section.Rows);
        Assert.Same(row, announced);
        Assert.Equal(1, changes);
        Assert.Equal((8, 10, 1), (row.FloorCount, row.ApartmentsPerFloor, row.UndergroundFloorCount));
    }

    [Fact]
    public void A_blank_added_row_goes_at_once_and_a_named_one_asks_first()
    {
        string? asked = null;
        var section = new PremisesSection("Objekte", new[] { Lindenallee }, () => { }, (question, _) => asked = question);
        section.AddCommand.Execute(null);

        section.RemoveCommand.Execute(section.Rows[1]);
        Assert.Single(section.Rows);
        Assert.Null(asked);

        section.RemoveCommand.Execute(section.Rows[0]);
        Assert.Equal("„Wohnanlage Lindenallee“ aus Objekte entfernen?", asked);
        Assert.Single(section.Rows);
    }
}
