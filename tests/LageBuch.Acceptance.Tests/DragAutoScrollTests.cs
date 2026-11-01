using LageBuch.App.Shared.Controls;

namespace LageBuch.Acceptance.Tests;

// #826: how far a drag near the edge of a scroller moves it per step.
public class DragAutoScrollTests
{
    [Fact]
    public void Step_is_zero_away_from_the_edges()
    {
        Assert.Equal(0.0, DragAutoScroll.Step(300, 600), precision: 6);
        Assert.Equal(0.0, DragAutoScroll.Step(48, 600), precision: 6);
        Assert.Equal(0.0, DragAutoScroll.Step(552, 600), precision: 6);
    }

    [Fact]
    public void Step_scrolls_back_near_the_start_and_forward_near_the_end()
    {
        Assert.True(DragAutoScroll.Step(10, 600) < 0);
        Assert.True(DragAutoScroll.Step(590, 600) > 0);
    }

    [Fact]
    public void Step_grows_the_closer_the_pointer_is_to_the_edge()
    {
        Assert.True(DragAutoScroll.Step(595, 600) > DragAutoScroll.Step(560, 600));
        Assert.True(DragAutoScroll.Step(5, 600) < DragAutoScroll.Step(40, 600));
        Assert.Equal(24.0, DragAutoScroll.Step(600, 600), precision: 6);
        Assert.Equal(-24.0, DragAutoScroll.Step(0, 600), precision: 6);
    }

    [Fact]
    public void Step_band_shrinks_on_a_small_viewport()
    {
        // 100 px tall: a quarter of it is edge, so the middle half still holds a pointer still.
        Assert.Equal(0.0, DragAutoScroll.Step(30, 100), precision: 6);
        Assert.True(DragAutoScroll.Step(20, 100) < 0);
        Assert.Equal(0.0, DragAutoScroll.Step(10, 0), precision: 6);
    }
}
