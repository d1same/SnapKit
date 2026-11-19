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
}
