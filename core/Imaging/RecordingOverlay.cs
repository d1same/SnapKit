namespace SnapKit;

public readonly record struct ClickPulse(double X, double Y, double AgeSeconds, bool Secondary);

/// <summary>
/// Pure-C# compositor for drawing a crisp pointer cursor and expanding click highlight rings
/// directly onto a BGRA PixelBuffer during screen recordings.
/// </summary>
public static class RecordingOverlay
{
    public const double PulseDurationSeconds = 0.38;

    // 14x20 clean classic arrow cursor bitmap: 0 = transparent, 1 = dark outline, 2 = white fill
    private static readonly string[] CursorMask =
    {
        "1.............",
        "11............",
        "121...........",
        "1221..........",
        "12221.........",
        "122221........",
        "1222221.......",
        "12222221......",
        "122222221.....",
        "1222222221....",
        "12222222221...",
        "122222222221..",
        "1222222111111.",
        "12221221......",
        "1221.1221.....",
        "121..1221.....",
        "11....1221....",
        "1.....1221....",
        ".......11....."
    };

    public static void DrawClickPulses(PixelBuffer buffer, IReadOnlyList<ClickPulse> pulses)
    {
        foreach (var pulse in pulses)
        {
            if (pulse.AgeSeconds < 0 || pulse.AgeSeconds >= PulseDurationSeconds)
                continue;
            double t = pulse.AgeSeconds / PulseDurationSeconds;
            double eased = 1.0 - Math.Pow(1.0 - t, 2.2);
            double radius = 6.0 + eased * 18.0;
            double alpha = (1.0 - t) * 0.85;
            if (alpha <= 0.02)
                continue;

            // Left click: warm amber-white highlight; Right click: soft coral highlight
            byte r = pulse.Secondary ? (byte)255 : (byte)255;
            byte g = pulse.Secondary ? (byte)107 : (byte)214;
            byte b = pulse.Secondary ? (byte)107 : (byte)64;

            DrawRing(buffer, pulse.X, pulse.Y, radius, thickness: 2.6, r, g, b, alpha);
            if (t < 0.45)
            {
                double innerAlpha = (1.0 - t / 0.45) * 0.28;
                DrawDisc(buffer, pulse.X, pulse.Y, Math.Max(3, radius * 0.55), r, g, b, innerAlpha);
            }
        }
    }

    public static void DrawCursor(PixelBuffer buffer, int x, int y, double scale = 1.0)
    {
        int factor = scale >= 1.75 ? 2 : 1;
        int rows = CursorMask.Length;
        for (int cy = 0; cy < rows; cy++)
        {
            string line = CursorMask[cy];
            for (int cx = 0; cx < line.Length; cx++)
            {
                char c = line[cx];
                if (c == '.')
                    continue;
                byte rgb = c == '2' ? (byte)250 : (byte)20;
                for (int sy = 0; sy < factor; sy++)
                {
                    int py = y + cy * factor + sy;
                    if ((uint)py >= (uint)buffer.Height)
                        continue;
                    for (int sx = 0; sx < factor; sx++)
                    {
                        int px = x + cx * factor + sx;
                        if ((uint)px >= (uint)buffer.Width)
                            continue;
                        buffer.SetPixel(px, py, rgb, rgb, rgb, 255);
                    }
                }
            }
        }
    }

    private static void DrawRing(PixelBuffer buffer, double cx, double cy, double radius, double thickness, byte r, byte g, byte b, double alpha)
    {
        double outer = radius + thickness * 0.5 + 1;
        int minX = Math.Max(0, (int)Math.Floor(cx - outer));
        int maxX = Math.Min(buffer.Width - 1, (int)Math.Ceiling(cx + outer));
        int minY = Math.Max(0, (int)Math.Floor(cy - outer));
        int maxY = Math.Min(buffer.Height - 1, (int)Math.Ceiling(cy + outer));
        double half = thickness * 0.5;

        for (int y = minY; y <= maxY; y++)
        {
            double dy = y - cy;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x - cx;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double edge = Math.Abs(dist - radius);
                if (edge > half + 1.0)
                    continue;
                double coverage = edge <= half ? 1.0 : Math.Clamp(1.0 - (edge - half), 0, 1);
                BlendPixel(buffer, x, y, r, g, b, alpha * coverage);
            }
        }
    }

    private static void DrawDisc(PixelBuffer buffer, double cx, double cy, double radius, byte r, byte g, byte b, double alpha)
    {
        int minX = Math.Max(0, (int)Math.Floor(cx - radius - 1));
        int maxX = Math.Min(buffer.Width - 1, (int)Math.Ceiling(cx + radius + 1));
        int minY = Math.Max(0, (int)Math.Floor(cy - radius - 1));
        int maxY = Math.Min(buffer.Height - 1, (int)Math.Ceiling(cy + radius + 1));

        for (int y = minY; y <= maxY; y++)
        {
            double dy = y - cy;
            for (int x = minX; x <= maxX; x++)
            {
                double dx = x - cx;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius + 1.0)
                    continue;
                double coverage = dist <= radius ? 1.0 : Math.Clamp(1.0 - (dist - radius), 0, 1);
                BlendPixel(buffer, x, y, r, g, b, alpha * coverage);
            }
        }
    }

    private static void BlendPixel(PixelBuffer buffer, int x, int y, byte r, byte g, byte b, double alpha)
    {
        if (alpha <= 0.001)
            return;
        int i = (y * buffer.Width + x) * 4;
        double inv = 1.0 - alpha;
        buffer.Pixels[i] = (byte)Math.Clamp(Math.Round(b * alpha + buffer.Pixels[i] * inv), 0, 255);
        buffer.Pixels[i + 1] = (byte)Math.Clamp(Math.Round(g * alpha + buffer.Pixels[i + 1] * inv), 0, 255);
        buffer.Pixels[i + 2] = (byte)Math.Clamp(Math.Round(r * alpha + buffer.Pixels[i + 2] * inv), 0, 255);
        buffer.Pixels[i + 3] = 255;
    }
}
