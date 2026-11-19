using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
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
        using var bitmap = await LoadSoftwareBitmap(working);
        var result = await engine.RecognizeAsync(bitmap);
        double scaleX = source.Width / (double)Math.Max(1, working.Width);
        double scaleY = source.Height / (double)Math.Max(1, working.Height);
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
                        words.Add(new OcrToken
                        {
                            Text = word.Text.Trim(),
                            Line = lineIndex,
                            LineText = lineText,
                            WordsInLine = count,
                            X = bounds.X * scaleX,
                            Y = bounds.Y * scaleY,
                            Width = bounds.Width * scaleX,
                            Height = bounds.Height * scaleY
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

    private static async Task<SoftwareBitmap> LoadSoftwareBitmap(PixelBuffer source)
    {
        var png = EncodePng(source);
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(png);
        await writer.StoreAsync();
        writer.DetachStream();
        writer.Dispose();
        stream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static byte[] EncodePng(PixelBuffer source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(BitmapConvert.ToBitmapSource(source)));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}
