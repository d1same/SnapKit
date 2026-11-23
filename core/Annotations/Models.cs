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
    Redact,
    Spotlight
}

public enum ShapeFillMode
{
    Outline,
    Tint,
    Solid
}

public enum BackdropPreset
{
    None,
    Clean,
    Dark,
    Slate
}

public readonly record struct BackdropMetrics(int PadX, int PadTop, int PadBottom, int CornerRadius, int TotalWidth, int TotalHeight);

public static class BackdropLayout
{
    public static BackdropMetrics Measure(BackdropPreset preset, int width, int height)
    {
        int w = Math.Max(1, width);
        int h = Math.Max(1, height);
        if (preset == BackdropPreset.None)
            return new BackdropMetrics(0, 0, 0, 0, w, h);

        int padX = Math.Clamp((int)Math.Round(Math.Min(w, h) * 0.08), 24, 48);
        int padTop = Math.Clamp((int)Math.Round(padX * 0.85), 20, 42);
        int padBottom = padX + 6;
        int radius = Math.Clamp((int)Math.Round(Math.Min(w, h) * 0.025), 8, 14);
        return new BackdropMetrics(padX, padTop, padBottom, radius, w + padX * 2, h + padTop + padBottom);
    }
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

    public IntRect Clamp(int boundsWidth, int boundsHeight, int minSize)
    {
        int limitW = Math.Max(1, boundsWidth);
        int limitH = Math.Max(1, boundsHeight);
        int minW = Math.Min(Math.Max(1, minSize), limitW);
        int minH = Math.Min(Math.Max(1, minSize), limitH);
        int width = Math.Clamp(Width, minW, limitW);
        int height = Math.Clamp(Height, minH, limitH);
        int x = Math.Clamp(X, 0, limitW - width);
        int y = Math.Clamp(Y, 0, limitH - height);
        return new IntRect(x, y, width, height);
    }
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
    public ShapeFillMode FillMode { get; set; } = ShapeFillMode.Outline;

    public Annotation Clone() => new()
    {
        Id = Id,
        Kind = Kind,
        Points = Points.Select(p => p).ToList(),
        ColorHex = ColorHex,
        Stroke = Stroke,
        Text = Text,
        Number = Number,
        FontSize = FontSize,
        FillMode = FillMode
    };
}
