using System.Windows.Media;

namespace SnapKit;

internal static class Palette
{
    public static readonly FontFamily Font = new("Segoe UI");
    public static readonly FontFamily Mono = new("Consolas");

    // Style 3 — Ultra-Minimal Rail neutral graphite palette
    public static readonly SolidColorBrush Bg = Freeze(0x15, 0x15, 0x15);
    public static readonly SolidColorBrush Card = Freeze(0x22, 0x22, 0x22);
    public static readonly SolidColorBrush Toolbar = Freeze(0x22, 0x22, 0x22, 0xF5);
    public static readonly SolidColorBrush Group = Freeze(0x1B, 0x1B, 0x1B);
    public static readonly SolidColorBrush Elevated = Freeze(0x2A, 0x2A, 0x2A);
    public static readonly SolidColorBrush Hover = Freeze(0x32, 0x32, 0x32);
    public static readonly SolidColorBrush Pressed = Freeze(0x3A, 0x3A, 0x3A);
    public static readonly SolidColorBrush Line = Freeze(0x2E, 0x2E, 0x2E);
    public static readonly SolidColorBrush LineStrong = Freeze(0x40, 0x40, 0x40);
    public static readonly SolidColorBrush Divider = Freeze(0x2E, 0x2E, 0x2E);
    public static readonly SolidColorBrush Text = Freeze(0xF2, 0xF2, 0xF2);
    public static readonly SolidColorBrush TextBright = Freeze(0xFF, 0xFF, 0xFF);
    public static readonly SolidColorBrush Muted = Freeze(0x8A, 0x8A, 0x8A);
    public static readonly SolidColorBrush Subtle = Freeze(0x66, 0x66, 0x66);
    public static readonly SolidColorBrush Ink = Freeze(0x15, 0x15, 0x15);
    public static readonly SolidColorBrush Field = Freeze(0x18, 0x18, 0x18);

    // Primary active surface is crisp white (#F2F2F2) with dark ink (#151515)
    public static readonly SolidColorBrush ActiveSurface = Text;
    public static readonly SolidColorBrush ActiveHover = TextBright;

    // Reserved strictly for "Copied!" feedback
    public static readonly SolidColorBrush Copied = Freeze(0x22, 0xC5, 0x5E);
    public static readonly SolidColorBrush CopiedSoft = Freeze(0x22, 0xC5, 0x5E, 0x24);

    public static readonly SolidColorBrush Warning = Freeze(0xD4, 0xA7, 0x2C);
    public static readonly SolidColorBrush Danger = Freeze(0xE5, 0x53, 0x53);
    public static readonly SolidColorBrush DangerSoft = Freeze(0xE5, 0x53, 0x53, 0x22);

    // Annotation ink swatches (for drawing on screenshots)
    public static readonly string[] Swatches =
    {
        "#FF3B30", "#FF9F0A", "#FFE14A", "#30D158", "#0A84FF", "#BF5AF2", "#F2F2F2", "#151515"
    };

    public static SolidColorBrush Freeze(byte r, byte g, byte b, byte a = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Color Rgb(string hex, byte alpha = 255)
    {
        if (!HexColor.TryParse(hex, out var r, out var g, out var b))
            return Color.FromArgb(alpha, 255, 59, 48);
        return Color.FromArgb(alpha, r, g, b);
    }

    public static SolidColorBrush Brush(string hex, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Rgb(hex, alpha));
        brush.Freeze();
        return brush;
    }
}
