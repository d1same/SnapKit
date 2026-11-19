using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace SnapKit;

internal static class OcrClient
{
    public static Task<OcrPage> RecognizeAsync(PixelBuffer source)
    {
        var copy = source.Clone();
        var done = new TaskCompletionSource<OcrPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Run(copy, done))
        {
            IsBackground = true,
            Name = "SnapKit OCR"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static void Run(PixelBuffer source, TaskCompletionSource<OcrPage> done)
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

    private static async Task<OcrPage> Recognize(PixelBuffer source)
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
        using var bitmap = CopySoftwareBitmap(working);
        var result = await engine.RecognizeAsync(bitmap);
        var words = new List<OcrToken>();
        if (result?.Lines != null)
        {
            int lineIndex = 0;
            foreach (var line in result.Lines)
            {
                int count = line.Words?.Count ?? 0;
                string lineText = line.Text ?? "";
                if (line.Words != null)
                {
                    foreach (var word in line.Words)
                    {
                        var bounds = word.BoundingRect;
                        if (bounds.Width < 1 || bounds.Height < 1 || string.IsNullOrWhiteSpace(word.Text))
                            continue;
                        var mapped = OcrMap.ToImagePixels(
                            new OcrBox(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                            bitmap.PixelWidth,
                            bitmap.PixelHeight,
                            source.Width,
                            source.Height);
                        words.Add(new OcrToken
                        {
                            Text = word.Text.Trim(),
                            Line = lineIndex,
                            LineText = lineText,
                            WordsInLine = count,
                            X = mapped.X,
                            Y = mapped.Y,
                            Width = mapped.Width,
                            Height = mapped.Height
                        });
                    }
                }
                lineIndex++;
            }
        }

        return new OcrPage
        {
            Text = (result?.Text ?? "").Trim(),
            Words = words
        };
    }

    private static SoftwareBitmap CopySoftwareBitmap(PixelBuffer source)
    {
        int count = source.Width * source.Height * 4;
        var bytes = new byte[count];
        System.Buffer.BlockCopy(source.Pixels, 0, bytes, 0, count);
        IBuffer buffer = CryptographicBuffer.CreateFromByteArray(bytes);
        return SoftwareBitmap.CreateCopyFromBuffer(
            buffer,
            BitmapPixelFormat.Bgra8,
            source.Width,
            source.Height,
            BitmapAlphaMode.Premultiplied);
    }
}
