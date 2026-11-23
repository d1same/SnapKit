using SnapKit;

namespace SnapKit.Tests;

public class SelectionAdjustTests
{
    [Fact]
    public void Move_stays_inside_the_screen()
    {
        var start = new IntRect(10, 20, 40, 30);
        var moved = SelectionAdjust.Apply(start, SelectionEdge.Move, 5, -4, 100, 80);
        Assert.Equal(new IntRect(15, 16, 40, 30), moved);

        var stuck = SelectionAdjust.Apply(start, SelectionEdge.Move, -100, 100, 100, 80);
        Assert.Equal(new IntRect(0, 50, 40, 30), stuck);
    }

    [Fact]
    public void Corner_drag_grows_and_shrinks_without_leaving_the_screen()
    {
        var start = new IntRect(20, 20, 40, 40);
        var bigger = SelectionAdjust.Apply(start, SelectionEdge.SouthEast, 15, 10, 200, 200);
        Assert.Equal(new IntRect(20, 20, 55, 50), bigger);

        var smaller = SelectionAdjust.Apply(start, SelectionEdge.SouthEast, -10, -12, 200, 200);
        Assert.Equal(new IntRect(20, 20, 30, 28), smaller);

        var clipped = SelectionAdjust.Apply(start, SelectionEdge.SouthEast, 500, 500, 100, 90);
        Assert.Equal(new IntRect(20, 20, 80, 70), clipped);
    }

    [Fact]
    public void Corner_drag_stops_at_the_minimum_size()
    {
        var start = new IntRect(20, 20, 40, 40);
        var stopped = SelectionAdjust.Apply(start, SelectionEdge.NorthWest, 70, 80, 200, 200);
        Assert.Equal(new IntRect(44, 44, SelectionAdjust.MinSize, SelectionAdjust.MinSize), stopped);
    }

    [Fact]
    public void Mid_edge_drag_resizes_single_axis_within_bounds()
    {
        var start = new IntRect(20, 20, 40, 40);

        Assert.Equal(new IntRect(20, 10, 40, 50), SelectionAdjust.Apply(start, SelectionEdge.North, 99, -10, 200, 200));
        Assert.Equal(new IntRect(20, 20, 40, 55), SelectionAdjust.Apply(start, SelectionEdge.South, -99, 15, 200, 200));
        Assert.Equal(new IntRect(20, 20, 65, 40), SelectionAdjust.Apply(start, SelectionEdge.East, 25, 99, 200, 200));
        Assert.Equal(new IntRect(5, 20, 55, 40), SelectionAdjust.Apply(start, SelectionEdge.West, -15, -99, 200, 200));

        // Clamps to min size and screen bounds
        Assert.Equal(new IntRect(44, 20, SelectionAdjust.MinSize, 40), SelectionAdjust.Apply(start, SelectionEdge.West, 100, 0, 200, 200));
        Assert.Equal(new IntRect(20, 20, 80, 40), SelectionAdjust.Apply(start, SelectionEdge.East, 500, 0, 100, 100));
    }
}
