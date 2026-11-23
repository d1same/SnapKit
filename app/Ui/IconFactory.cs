using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

namespace SnapKit;

internal static class IconFactory
{
    public static Icon Load()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SnapKit.Assets.snapkit.ico");
        if (stream != null)
            return new Icon(stream);
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(255, 17, 17, 17));
            using var pen = new Pen(Color.White, 2);
            graphics.DrawLine(pen, 6, 8, 6, 6);
            graphics.DrawLine(pen, 6, 6, 12, 6);
            graphics.DrawLine(pen, 20, 6, 26, 6);
            graphics.DrawLine(pen, 26, 6, 26, 12);
            graphics.DrawLine(pen, 6, 20, 6, 26);
            graphics.DrawLine(pen, 6, 26, 12, 26);
            graphics.DrawLine(pen, 20, 26, 26, 26);
            graphics.DrawLine(pen, 26, 20, 26, 26);
        }
        return FromBitmap(bitmap);
    }

    public static Icon LoadWithUpdateDot()
    {
        using var baseIcon = Load();
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawIcon(baseIcon, new Rectangle(0, 0, 32, 32));
            // Electric-blue update badge dot with dark cutout ring
            using var ring = new SolidBrush(Color.FromArgb(255, 17, 18, 22));
            using var dot = new SolidBrush(Color.FromArgb(255, 10, 132, 255));
            g.FillEllipse(ring, 18, 0, 14, 14);
            g.FillEllipse(dot, 20, 2, 10, 10);
        }
        return FromBitmap(bitmap);
    }

    private static Icon FromBitmap(Bitmap bitmap)
    {
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
