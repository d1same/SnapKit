using System.Windows;
using System.Windows.Media;

namespace SnapKit;

internal static class Palette
{
    public static readonly FontFamily Font = new("Segoe UI");
    public static readonly FontFamily Mono = new("Consolas");
    public static readonly SolidColorBrush Bg = Freeze(0x11, 0x11, 0x11);
    public static readonly SolidColorBrush Card = Freeze(0x18, 0x18, 0x18);
    public static readonly SolidColorBrush Elevated = Freeze(0x2A, 0x2A, 0x2A);
    public static readonly SolidColorBrush Hover = Freeze(0x34, 0x34, 0x34);
    public static readonly SolidColorBrush Line = Freeze(0x3A, 0x3A, 0x3A);
    public static readonly SolidColorBrush Text = Freeze(0xF4, 0xF4, 0xF5);
    public static readonly SolidColorBrush Muted = Freeze(0xA1, 0xA1, 0xAA);
    public static readonly SolidColorBrush Ink = Freeze(0x11, 0x11, 0x11);
    public static readonly SolidColorBrush Field = Freeze(0x10, 0x10, 0x10);
    public static readonly SolidColorBrush Danger = Freeze(0xFF, 0x5A, 0x5A);

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
