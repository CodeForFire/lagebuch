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
        var street = Label("Hauptstraße", new Rect(100, 100, 80, 12), priority: 2);
        var town = Label("Musterstadt", new Rect(120, 104, 90, 16), priority: 0);

        var placed = MapDrawing.PlaceLabels([(street, default), (town, default)], Screen);

        Assert.Equal(["Musterstadt"], placed.Select(p => p.Label.Text));
    }

    [AvaloniaFact]
    public void Names_that_only_come_close_are_both_drawn()
    {
        var a = Label("Musterstadt", new Rect(100, 100, 90, 16), priority: 0);
        var b = Label("Mühlweg", new Rect(100, 120, 60, 12), priority: 2);

        Assert.Equal(2, MapDrawing.PlaceLabels([(a, default), (b, default)], Screen).Count);
    }

    [AvaloniaFact]
    public void Labels_of_neighbouring_tiles_collide_in_screen_pixels()
    {
        // The same spot on the screen, reached from the tile to the left and from the tile itself.
        var fromLeftTile = Label("Musterstadt", new Rect(240, 100, 90, 16), priority: 0);
        var fromThisTile = Label("Musterbach", new Rect(0, 100, 70, 12), priority: 1);

        var placed = MapDrawing.PlaceLabels([(fromLeftTile, new Vector(0, 0)), (fromThisTile, new Vector(256, 0))], Screen);

        Assert.Equal(["Musterstadt"], placed.Select(p => p.Label.Text));
    }

    [AvaloniaFact]
    public void A_street_named_again_on_the_next_tile_is_named_once_and_again_further_on()
    {
        var here = Label("Hauptstraße", new Rect(100, 100, 80, 12), priority: 2);
        var nextTile = Label("Hauptstraße", new Rect(100, 100, 80, 12), priority: 2);
        var farOn = Label("Hauptstraße", new Rect(100, 100, 80, 12), priority: 2);

        var placed = MapDrawing.PlaceLabels([(here, default), (nextTile, new Vector(200, 0)), (farOn, new Vector(MapDrawing.RepeatDistance + 1, 0))], Screen);

        Assert.Equal([default, new Vector(MapDrawing.RepeatDistance + 1, 0)], placed.Select(p => p.Offset));
    }

    [AvaloniaFact]
    public void A_label_wholly_off_the_screen_is_skipped_and_one_reaching_onto_it_is_drawn()
    {
        var off = Label("Grafrath", new Rect(-100, 100, 80, 12), priority: 0);
        var reaching = Label("Kottgeisering", new Rect(-40, 200, 90, 12), priority: 0);

        var placed = MapDrawing.PlaceLabels([(off, default), (reaching, default)], Screen);

        Assert.Equal(["Kottgeisering"], placed.Select(p => p.Label.Text));
    }

    private static MapLabel Label(string text, Rect bounds, int priority) =>
        new(text, new StreamGeometry(), Matrix.CreateTranslation(bounds.X, bounds.Y), bounds, priority, Brushes.Black, null);
}
