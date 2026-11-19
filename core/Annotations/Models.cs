namespace SnapKit;

public enum AnnotationKind
{
    Pen,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Highlighter,
    Text,
    Callout,
    Number,
    Blur,
    Spotlight
}

public readonly record struct PointD(double X, double Y);

public readonly record struct IntRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public IntRect Intersect(IntRect other)
    {
        int x1 = Math.Max(X, other.X);
        int y1 = Math.Max(Y, other.Y);
        int x2 = Math.Min(Right, other.Right);
        int y2 = Math.Min(Bottom, other.Bottom);
        return new IntRect(x1, y1, Math.Max(0, x2 - x1), Math.Max(0, y2 - y1));
    }

    public bool Contains(double x, double y) => x >= X && y >= Y && x < Right && y < Bottom;
}

public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AnnotationKind Kind { get; set; }
    public List<PointD> Points { get; set; } = new();
    public string ColorHex { get; set; } = "#FF3B30";
    public double Stroke { get; set; } = 3;
    public string Text { get; set; } = "";
    public int Number { get; set; }
    public double FontSize { get; set; } = 18;

    public Annotation Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Points = Points.Select(p => p).ToList(),
        ColorHex = ColorHex,
        Stroke = Stroke,
        Text = Text,
        Number = Number,
        FontSize = FontSize
    };
}
