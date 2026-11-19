using System.Drawing;
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
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
