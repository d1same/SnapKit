using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal static class ImageFile
{
    public static void Copy(BitmapSource image)
    {
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        png.Position = 0;

        var data = new DataObject();
        data.SetData(DataFormats.Bitmap, image, true);
        data.SetData("PNG", png, false);
        Clipboard.SetDataObject(data, true);
    }

    public static bool Save(BitmapSource image, Window owner)
    {
        bool wasTopmost = owner.Topmost;
        owner.Topmost = false;
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save capture",
                Filter = "PNG image|*.png|JPEG image|*.jpg",
                FileName = $"{Brand.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.png",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(owner) != true)
                return false;

            using var stream = File.Create(dialog.FileName);
            BitmapEncoder encoder = dialog.FilterIndex == 2
                ? new JpegBitmapEncoder { QualityLevel = 92 }
                : new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(stream);
            return true;
        }
        finally
        {
            owner.Topmost = wasTopmost;
        }
    }
}
