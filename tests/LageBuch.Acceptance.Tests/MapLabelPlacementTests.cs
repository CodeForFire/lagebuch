using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using LageBuch.App.Shared.Controls;

namespace LageBuch.Acceptance.Tests;

public class MapLabelPlacementTests
{
    private static readonly Size Screen = new(800, 500);

    [AvaloniaFact]
    public void Of_two_overlapping_names_the_more_important_one_is_drawn()
    {
        var street = Label("Hauptstraße", new Size(80, 12), priority: 2);
        var town = Label("Musterstadt", new Size(90, 16), priority: 0);

        var placed = MapDrawing.PlaceLabels([(street, new Point(140, 106)), (town, new Point(165, 112))], Screen);

        Assert.Equal(["Musterstadt"], placed.Select(p => p.Label.Text));
    }

    [AvaloniaFact]
    public void Names_that_only_come_close_are_both_drawn()
    {
        var a = Label("Musterstadt", new Size(90, 16), priority: 0);
        var b = Label("Mühlweg", new Size(60, 12), priority: 2);

        Assert.Equal(2, MapDrawing.PlaceLabels([(a, new Point(145, 108)), (b, new Point(130, 126))], Screen).Count);
    }

    [AvaloniaFact]
    public void A_street_named_again_close_by_is_named_once_and_again_further_on()
    {
        var label = Label("Hauptstraße", new Size(80, 12), priority: 2);
        var farOn = new Point(140 + MapDrawing.RepeatDistance + 1, 106);

        var placed = MapDrawing.PlaceLabels([(label, new Point(140, 106)), (label, new Point(340, 106)), (label, farOn)], Screen);

        Assert.Equal([new Point(140, 106), farOn], placed.Select(p => p.Placement.Transform(new Point(40, 6))));
    }

    [AvaloniaFact]
    public void A_label_wholly_off_the_screen_is_skipped_and_one_reaching_onto_it_is_drawn()
    {
        var off = Label("Grafrath", new Size(80, 12), priority: 0);
        var reaching = Label("Kottgeisering", new Size(90, 12), priority: 0);

        var placed = MapDrawing.PlaceLabels([(off, new Point(-60, 106)), (reaching, new Point(5, 206))], Screen);

        Assert.Equal(["Kottgeisering"], placed.Select(p => p.Label.Text));
    }

    [AvaloniaFact]
    public void A_name_under_a_hydrant_s_label_is_left_out()
    {
        var street = Label("Hauptstraße", new Size(80, 12), priority: 2);

        var placed = MapDrawing.PlaceLabels([(street, new Point(140, 106))], Screen, reserved: [new Rect(150, 98, 40, 16)]);

        Assert.Empty(placed);
    }

    [AvaloniaFact]
    public void A_name_beside_a_hydrant_s_label_is_drawn()
    {
        var street = Label("Hauptstraße", new Size(80, 12), priority: 2);

        // The street ends at x 180; the label starts clear of the gap kept around a name.
        var placed = MapDrawing.PlaceLabels([(street, new Point(140, 106))], Screen, reserved: [new Rect(183, 98, 40, 16)]);

        Assert.Single(placed);
    }

    [AvaloniaTheory]
    [InlineData(0, 0)]
    [InlineData(170, -10)] // turned nearly upside down, the name is flipped to read left to right
    [InlineData(-120, 60)]
    public void A_street_name_turns_with_the_map_and_never_reads_upside_down(double rotation, double expectedDegrees)
    {
        // A street running east on its tile.
        var street = Label("Hauptstraße", new Size(80, 12), priority: 2, angle: 0);

        var (_, placement) = Assert.Single(MapDrawing.PlaceLabels([(street, new Point(400, 250))], Screen, rotation));

        Assert.Equal(expectedDegrees, DirectionOf(placement), precision: 6);
    }

    [AvaloniaFact]
    public void A_place_name_stays_level_on_a_turned_map()
    {
        var town = Label("Musterstadt", new Size(90, 16), priority: 0);

        var (_, placement) = Assert.Single(MapDrawing.PlaceLabels([(town, new Point(400, 250))], Screen, rotation: 75));

        Assert.Equal(0, DirectionOf(placement), precision: 6);
    }

    // The way the text's baseline runs on screen, in degrees clockwise from east.
    private static double DirectionOf(Matrix placement)
    {
        var run = placement.Transform(new Point(1, 0)) - placement.Transform(default);
        return Math.Atan2(run.Y, run.X) * 180 / Math.PI;
    }

    private static MapLabel Label(string text, Size size, int priority, double? angle = null) =>
        new(text, new StreamGeometry(), size, default, angle, priority, Brushes.Black, null);
}
