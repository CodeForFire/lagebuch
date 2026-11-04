using Avalonia;
using LageBuch.App.Shared.Controls;
using LageBuch.Domain.ValueObjects;

namespace LageBuch.Acceptance.Tests;

public class SignPlacementTests
{
    private static readonly Point Pin = new(500, 400);
    private static readonly Size SignSize = new(200, 58);
    private static readonly Point Anchor = TacticalSignControl.SymbolCenter;

    [Fact]
    public void An_uncrowded_sign_stands_up_and_to_the_right_of_its_pin()
    {
        var offset = SignPlacement.Choose(Pin, SignSize, Anchor, [SignPlacement.PinBox(Pin)], []);
        var box = SignPlacement.Box(Pin, offset, SignSize, Anchor);

        Assert.True(box.Left > Pin.X, $"{box} is not right of the pin");
        Assert.True(box.Bottom < Pin.Y, $"{box} is not above the pin");
        Assert.False(box.Intersects(SignPlacement.PinBox(Pin)));
    }

    // #885: the ESt pin's head stands above the Einsatzstelle, its tip on it.
    [Fact]
    public void The_Einsatzort_pin_box_stands_above_the_spot_centred_on_it()
    {
        var box = SignPlacement.EinsatzortPinBox(Pin);

        Assert.Equal(new Rect(480, 352, 40, 48), box);
    }

    [Fact]
    public void A_sign_left_only_the_place_above_keeps_clear_of_the_Einsatzort_pin_s_head()
    {
        // Right, left and below are taken, so straight above is the first place left, and it
        // covers the head of the ESt pin unless that head is kept clear.
        Rect[] crowd = [SignPlacement.PinBox(Pin), new Rect(Pin.X + 30, 0, 1000, 1000), new Rect(0, 0, Pin.X - 30, 1000), new Rect(0, Pin.Y + 5, 2000, 1000)];
        var head = SignPlacement.EinsatzortPinBox(Pin);
        var blind = SignPlacement.Box(Pin, SignPlacement.Choose(Pin, SignSize, Anchor, crowd, []), SignSize, Anchor);
        Assert.True(blind.Intersects(head), $"{blind} would not reach the ESt pin anyway");

        var box = SignPlacement.Box(Pin, SignPlacement.Choose(Pin, SignSize, Anchor, [.. crowd, head], []), SignSize, Anchor);

        Assert.False(box.Intersects(head), $"{box} covers the ESt pin");
        Assert.True(box.Bottom < head.Top, $"{box} is not above the ESt pin");
    }

    [Fact]
    public void A_sign_on_the_left_keeps_its_whole_width_clear_of_its_pin()
    {
        // Everything right of the pin is taken.
        Rect[] occupied = [SignPlacement.PinBox(Pin), new Rect(Pin.X, 0, 1000, 1000)];

        var box = SignPlacement.Box(Pin, SignPlacement.Choose(Pin, SignSize, Anchor, occupied, []), SignSize, Anchor);

        Assert.True(box.Right < Pin.X, $"{box} reaches back over the pin");
    }

    [Fact]
    public void A_taken_place_sends_the_sign_to_the_next_free_one()
    {
        var first = SignPlacement.Box(Pin, SignPlacement.Choose(Pin, SignSize, Anchor, [SignPlacement.PinBox(Pin)], []), SignSize, Anchor);

        var second = SignPlacement.Box(Pin, SignPlacement.Choose(Pin, SignSize, Anchor, [SignPlacement.PinBox(Pin), first], []), SignSize, Anchor);

        Assert.False(first.Intersects(second));
    }

    [Fact]
    public void A_place_whose_strich_would_cross_another_is_passed_over()
    {
        // A strich from below-right of the pin up and to the left, right across the way to the
        // place the pin's own sign would take first.
        var other = (From: new Point(Pin.X + 100, Pin.Y + 20), To: new Point(Pin.X + 20, Pin.Y - 150));

        var offset = SignPlacement.Choose(Pin, SignSize, Anchor, [SignPlacement.PinBox(Pin)], [other]);
        var end = new Point(Pin.X + offset.Dx, Pin.Y + offset.Dy);

        Assert.False(end.X > Pin.X && end.Y < Pin.Y, $"the sign still went up and right, to {end}");
    }

    [Fact]
    public void Where_no_place_is_free_the_sign_takes_the_least_covered_one_instead_of_failing()
    {
        // The whole screen is taken: there is nothing free, only less and more covered.
        Rect[] occupied = [new Rect(-5000, -5000, 10000, 10000)];

        var offset = SignPlacement.Choose(Pin, SignSize, Anchor, occupied, []);

        Assert.InRange(Math.Abs(offset.Dx), 0, LabelOffset.MaxPixels);
        Assert.InRange(Math.Abs(offset.Dy), 0, LabelOffset.MaxPixels);
    }

    [Fact]
    public void The_same_crowd_gets_the_same_places_every_time()
    {
        Rect[] occupied = [SignPlacement.PinBox(Pin), new Rect(Pin.X + 20, Pin.Y - 100, 220, 80)];

        Assert.Equal(
            SignPlacement.Choose(Pin, SignSize, Anchor, occupied, []),
            SignPlacement.Choose(Pin, SignSize, Anchor, occupied, []));
    }
}
