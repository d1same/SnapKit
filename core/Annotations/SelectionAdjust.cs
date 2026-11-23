namespace SnapKit;

public enum SelectionEdge
{
    Move,
    NorthWest,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West
}

public static class SelectionAdjust
{
    public const int MinSize = 16;

    public static IntRect Apply(IntRect start, SelectionEdge edge, int dx, int dy, int boundsWidth, int boundsHeight)
    {
        if (edge == SelectionEdge.Move)
            return new IntRect(start.X + dx, start.Y + dy, start.Width, start.Height).Clamp(boundsWidth, boundsHeight, MinSize);

        int min = Math.Min(MinSize, Math.Min(Math.Max(1, boundsWidth), Math.Max(1, boundsHeight)));
        int left = start.X;
        int top = start.Y;
        int right = start.Right;
        int bottom = start.Bottom;
        switch (edge)
        {
            case SelectionEdge.NorthWest:
                left = Math.Clamp(start.X + dx, 0, start.Right - min);
                top = Math.Clamp(start.Y + dy, 0, start.Bottom - min);
                break;
            case SelectionEdge.North:
                top = Math.Clamp(start.Y + dy, 0, start.Bottom - min);
                break;
            case SelectionEdge.NorthEast:
                right = Math.Clamp(start.Right + dx, start.X + min, boundsWidth);
                top = Math.Clamp(start.Y + dy, 0, start.Bottom - min);
                break;
            case SelectionEdge.East:
                right = Math.Clamp(start.Right + dx, start.X + min, boundsWidth);
                break;
            case SelectionEdge.SouthEast:
                right = Math.Clamp(start.Right + dx, start.X + min, boundsWidth);
                bottom = Math.Clamp(start.Bottom + dy, start.Y + min, boundsHeight);
                break;
            case SelectionEdge.South:
                bottom = Math.Clamp(start.Bottom + dy, start.Y + min, boundsHeight);
                break;
            case SelectionEdge.SouthWest:
                left = Math.Clamp(start.X + dx, 0, start.Right - min);
                bottom = Math.Clamp(start.Bottom + dy, start.Y + min, boundsHeight);
                break;
            case SelectionEdge.West:
                left = Math.Clamp(start.X + dx, 0, start.Right - min);
                break;
        }

        return new IntRect(left, top, Math.Max(min, right - left), Math.Max(min, bottom - top));
    }
}
