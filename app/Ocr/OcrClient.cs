using System.Runtime.InteropServices;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapKit;

internal static class OcrClient
{
    public static Task<string> RecognizeAsync(PixelBuffer source)
    {
        var copy = source.Clone();
        var done = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(copy, done))
        {
            IsBackground = true,
            Name = "SnapKit OCR"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static void Run(PixelBuffer source, TaskCompletionSource<string> done)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                done.TrySetResult(await Recognize(source));
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
            finally
            {
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
    }

    private static async Task<string> Recognize(PixelBuffer source)
    {
        if (source.Width < 3 || source.Height < 3)
            throw new InvalidOperationException("That area is too small to read.");

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
            throw new InvalidOperationException("Windows OCR has no language installed. Add one in Settings, under Time & language, then Language & region.");

        var working = source;
        const int maxEdge = 4000;
        if (source.Width > maxEdge || source.Height > maxEdge)
        {
            double scale = Math.Min(maxEdge / (double)source.Width, maxEdge / (double)source.Height);
            working = source.Resize(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        }

        working.ForceOpaque();
        using var bitmap = ToSoftwareBitmap(working);
        var result = await engine.RecognizeAsync(bitmap);
        return (result.Text ?? "").Trim();
    }

    private static SoftwareBitmap ToSoftwareBitmap(PixelBuffer source)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, source.Width, source.Height, BitmapAlphaMode.Straight);
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Write);
        using var reference = buffer.CreateReference();
        ((IMemoryBufferByteAccess)reference).GetBuffer(out IntPtr data, out uint capacity);
        var plane = buffer.GetPlaneDescription(0);
        int rowBytes = source.Width * 4;
        for (int y = 0; y < source.Height; y++)
        {
            int destination = plane.StartIndex + y * plane.Stride;
            if (destination < 0 || destination + rowBytes > capacity)
                break;
            Marshal.Copy(source.Pixels, y * source.Stride, data + destination, rowBytes);
        }
        return bitmap;
    }

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }
}
