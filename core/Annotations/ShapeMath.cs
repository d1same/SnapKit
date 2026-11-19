namespace SnapKit;

public static class ShapeMath
{
    public static PointD ConstrainLine(PointD start, PointD end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.001)
            return end;
        double angle = Math.Atan2(dy, dx);
        double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        return new PointD(start.X + Math.Cos(snapped) * len, start.Y + Math.Sin(snapped) * len);
    }

    public static PointD ConstrainSquare(PointD start, PointD end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        double sx = dx < 0 ? -1 : 1;
        double sy = dy < 0 ? -1 : 1;
        if (dx == 0) sx = 1;
        if (dy == 0) sy = 1;
        return new PointD(start.X + sx * side, start.Y + sy * side);
    }

    public static (PointD Left, PointD Right) ArrowHead(PointD start, PointD end, double length)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double mag = Math.Sqrt(dx * dx + dy * dy);
        if (mag < 0.001)
            return (end, end);
        double ux = dx / mag;
        double uy = dy / mag;
        double spread = length * 0.55;
        var basePoint = new PointD(end.X - ux * length, end.Y - uy * length);
        var left = new PointD(basePoint.X + -uy * spread, basePoint.Y + ux * spread);
        var right = new PointD(basePoint.X - -uy * spread, basePoint.Y - ux * spread);
        return (left, right);
    }

    public static (double X, double Y, double W, double H) Bounds(IReadOnlyList<PointD> points, double pad = 0)
    {
        if (points.Count == 0)
            return (0, 0, 0, 0);
        double minX = points.Min(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxX = points.Max(p => p.X);
        double maxY = points.Max(p => p.Y);
        return (minX - pad, minY - pad, Math.Max(1, maxX - minX) + pad * 2, Math.Max(1, maxY - minY) + pad * 2);
    }

    public static bool IsDark(string hex)
    {
        if (!HexColor.TryParse(hex, out var r, out var g, out var b))
            return true;
        double l = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        return l < 150;
    }
}

public static class HexColor
{
    public static bool TryParse(string? hex, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        hex = hex.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length != 6)
            return false;
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int value))
            return false;
        r = (byte)((value >> 16) & 255);
        g = (byte)((value >> 8) & 255);
        b = (byte)(value & 255);
        return true;
    }

    public static string Normalize(string? hex, string fallback = "#FF3B30")
    {
        return TryParse(hex, out var r, out var g, out var b) ? $"#{r:X2}{g:X2}{b:X2}" : fallback;
    }
}
