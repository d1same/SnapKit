using System.Windows;
using System.Windows.Media;

namespace SnapKit;

internal static class Palette
{
    public static readonly FontFamily Font = new("Segoe UI");
    public static readonly FontFamily Mono = new("Consolas");

    public static readonly SolidColorBrush Bg = Freeze(0x11, 0x12, 0x16);
    public static readonly SolidColorBrush Card = Freeze(0x19, 0x1B, 0x21);
    public static readonly SolidColorBrush Toolbar = Freeze(0x18, 0x1A, 0x20, 0xF5);
    public static readonly SolidColorBrush Group = Freeze(0x22, 0x25, 0x2E);
    public static readonly SolidColorBrush Elevated = Freeze(0x27, 0x2B, 0x35);
    public static readonly SolidColorBrush Hover = Freeze(0x32, 0x37, 0x45);
    public static readonly SolidColorBrush Line = Freeze(0x33, 0x38, 0x45);
    public static readonly SolidColorBrush Divider = Freeze(0x2A, 0x2E, 0x3A);
    public static readonly SolidColorBrush Text = Freeze(0xF4, 0xF5, 0xF7);
    public static readonly SolidColorBrush Muted = Freeze(0x9E, 0xA4, 0xB3);
    public static readonly SolidColorBrush Subtle = Freeze(0x74, 0x7B, 0x8C);
    public static readonly SolidColorBrush Ink = Freeze(0x0E, 0x10, 0x14);
    public static readonly SolidColorBrush Field = Freeze(0x13, 0x15, 0x1A);
    public static readonly SolidColorBrush Accent = Freeze(0x0A, 0x84, 0xFF);
    public static readonly SolidColorBrush AccentHover = Freeze(0x2E, 0x97, 0xFF);
    public static readonly SolidColorBrush AccentSoft = Freeze(0x0A, 0x84, 0xFF, 0x2E);
    public static readonly SolidColorBrush AccentLine = Freeze(0x0A, 0x84, 0xFF, 0x88);
    public static readonly SolidColorBrush Success = Freeze(0x30, 0xD1, 0x58);
    public static readonly SolidColorBrush Warning = Freeze(0xFF, 0x9F, 0x0A);
    public static readonly SolidColorBrush Danger = Freeze(0xFF, 0x5A, 0x5A);
    public static readonly SolidColorBrush DangerSoft = Freeze(0xFF, 0x5A, 0x5A, 0x26);

    public static readonly string[] Swatches =
    {
        "#FF3B30", "#FF9F0A", "#FFE14A", "#30D158", "#0A84FF", "#BF5AF2", "#F4F4F5", "#1C1C1E"
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
