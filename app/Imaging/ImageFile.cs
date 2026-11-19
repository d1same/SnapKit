using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal static class ImageFile
{
    public static void Copy(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];
        bgra.CopyPixels(pixels, stride, 0);

        byte[] pngBytes;
        using (var png = new MemoryStream())
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            pngBytes = png.ToArray();
        }

        var data = new DataObject();
        data.SetData(DataFormats.Dib, new MemoryStream(DibImage.FromTopDownBgra(pixels, width, height, stride)), false);
        data.SetData("PNG", new MemoryStream(pngBytes), false);
        data.SetData("image/png", new MemoryStream(pngBytes), false);
        SetClipboard(data);
    }

    private static void SetClipboard(DataObject data)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return;
            }
            catch (Exception) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
        }
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
