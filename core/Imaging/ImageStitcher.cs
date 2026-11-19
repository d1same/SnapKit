namespace SnapKit;

/// <summary>
/// Stitches scrolled window frames into one tall image.
/// Scrolling down moves existing content up, so the bottom of the previous
/// viewport matches the top of the next one. Fixed headers and footers are
/// the rows that do not change between the first two frames.
/// </summary>
public static class ImageStitcher
{
    public readonly record struct ChangeBand(int Top, int Bottom)
    {
        public int Height => Math.Max(0, Bottom - Top);
        public bool IsEmpty => Height < 8;
        public static ChangeBand Empty => new(0, 0);
    }

    public readonly record struct ScrollMatch(bool Success, int Delta, double MeanError);

    public static ChangeBand DetectChangedBand(PixelBuffer previous, PixelBuffer next, int ignoreRightPx = 0, int threshold = 12)
    {
        if (previous.Width != next.Width || previous.Height != next.Height)
            throw new ArgumentException("Frames must be the same size.");

        int height = previous.Height;
        var changed = new bool[height];
        for (int y = 0; y < height; y++)
            changed[y] = RowChanged(previous, next, y, ignoreRightPx, threshold);

        for (int y = 1; y < height - 1; y++)
        {
            if (!changed[y] && changed[y - 1] && changed[y + 1])
                changed[y] = true;
        }

        int bestStart = 0, bestLen = 0, start = -1;
        for (int y = 0; y <= height; y++)
        {
            bool on = y < height && changed[y];
            if (on)
            {
                if (start < 0)
                    start = y;
            }
            else if (start >= 0)
            {
                int len = y - start;
                if (len > bestLen)
                {
                    bestLen = len;
                    bestStart = start;
                }
                start = -1;
            }
        }

        if (bestLen < 8)
            return ChangeBand.Empty;
        return new ChangeBand(bestStart, bestStart + bestLen);
    }

    public static ScrollMatch FindScroll(PixelBuffer previous, PixelBuffer next, ChangeBand band, int ignoreRightPx = 0)
    {
        int rows = band.Height;
        if (rows < 8)
            return new ScrollMatch(false, 0, double.MaxValue);

        int sampleCount = Math.Clamp((previous.Width - Math.Max(0, ignoreRightPx)) / 8, 8, 48);
        var prev = SampleBand(previous, band, ignoreRightPx, sampleCount);
        var nextS = SampleBand(next, band, ignoreRightPx, sampleCount);
        int channels = sampleCount * 3;

        double identical = MeanShift(prev, nextS, 0, rows, channels);
        if (identical < 6)
            return new ScrollMatch(false, 0, identical);

        int maxDelta = rows - 4;
        int bestDelta = 0;
        double best = double.MaxValue;
        // Every row. A coarse step misses a sharp match (text rows look
        // nothing like their neighbors, so the true overlap is a one-row spike).
        for (int delta = 1; delta <= maxDelta; delta++)
        {
            double score = MeanShift(prev, nextS, delta, rows, channels);
            if (score < best)
            {
                best = score;
                bestDelta = delta;
            }
        }

        if (bestDelta <= 0 || best > 22 || best > identical * 0.72)
            return new ScrollMatch(false, 0, best);

        return new ScrollMatch(true, bestDelta, best);
    }

    public static PixelBuffer AppendNewRows(PixelBuffer content, PixelBuffer nextFrame, ChangeBand band, int delta)
    {
        if (delta <= 0)
            return content.Clone();
        if (delta > band.Height)
            delta = band.Height;
        var result = new PixelBuffer(content.Width, content.Height + delta);
        result.CopyPixelsFrom(content, 0, 0, 0, 0, content.Width, content.Height);
        int srcY = band.Bottom - delta;
        result.CopyPixelsFrom(nextFrame, 0, srcY, 0, content.Height, Math.Min(content.Width, nextFrame.Width), delta);
        return result;
    }

    public static PixelBuffer Compose(PixelBuffer? header, PixelBuffer content, PixelBuffer? footer)
    {
        int height = content.Height
            + (header?.Height ?? 0)
            + (footer?.Height ?? 0);
        var result = new PixelBuffer(content.Width, Math.Max(1, height));
        int y = 0;
        if (header is { Height: > 0 })
        {
            result.CopyPixelsFrom(header, 0, 0, 0, y, Math.Min(content.Width, header.Width), header.Height);
            y += header.Height;
        }
        result.CopyPixelsFrom(content, 0, 0, 0, y, content.Width, content.Height);
        y += content.Height;
        if (footer is { Height: > 0 })
            result.CopyPixelsFrom(footer, 0, 0, 0, y, Math.Min(content.Width, footer.Width), footer.Height);
        return result;
    }

    /// <summary>Stitch a sequence of same-sized frames. Used by tests and by the scroll loop's first pair.</summary>
    public static PixelBuffer StitchFrames(IReadOnlyList<PixelBuffer> frames, int ignoreRightPx = 0)
    {
        if (frames.Count == 0)
            throw new ArgumentException("Need at least one frame.", nameof(frames));
        if (frames.Count == 1)
            return frames[0].Clone();

        var band = DetectChangedBand(frames[0], frames[1], ignoreRightPx);
        if (band.IsEmpty)
            return frames[0].Clone();

        PixelBuffer? header = band.Top > 0 ? frames[0].Crop(0, 0, frames[0].Width, band.Top) : null;
        int footerHeight = frames[0].Height - band.Bottom;
        PixelBuffer? footer = footerHeight > 0 ? frames[0].Crop(0, band.Bottom, frames[0].Width, footerHeight) : null;
        var content = frames[0].Crop(0, band.Top, frames[0].Width, band.Height);

        var previous = frames[0];
        for (int i = 1; i < frames.Count; i++)
        {
            if (frames[i].Width != previous.Width || frames[i].Height != previous.Height)
                break;
            var match = FindScroll(previous, frames[i], band, ignoreRightPx);
            if (!match.Success)
                break;
            content = AppendNewRows(content, frames[i], band, match.Delta);
            previous = frames[i];
        }

        return Compose(header, content, footer);
    }

    private static bool RowChanged(PixelBuffer a, PixelBuffer b, int y, int ignoreRightPx, int threshold)
    {
        int x1 = a.Width - Math.Max(0, ignoreRightPx);
        if (x1 < 4)
            x1 = a.Width;
        const int segments = 8;
        int segW = Math.Max(1, x1 / segments);
        for (int s = 0; s < segments; s++)
        {
            int x0 = s * segW;
            int xEnd = s == segments - 1 ? x1 : Math.Min(x1, x0 + segW);
            long sum = 0;
            int n = 0;
            int step = Math.Max(1, (xEnd - x0) / 16);
            int row = y * a.Stride;
            for (int x = x0; x < xEnd; x += step)
            {
                int i = row + x * 4;
                sum += Math.Abs(a.Pixels[i] - b.Pixels[i]);
                sum += Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1]);
                sum += Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]);
                n += 3;
            }
            if (n > 0 && sum / n > threshold)
                return true;
        }
        return false;
    }

    private static byte[] SampleBand(PixelBuffer image, ChangeBand band, int ignoreRight, int sampleCount)
    {
        int x1 = Math.Max(1, image.Width - Math.Max(0, ignoreRight));
        int channels = sampleCount * 3;
        var data = new byte[band.Height * channels];
        for (int row = 0; row < band.Height; row++)
        {
            int y = band.Top + row;
            int dest = row * channels;
            for (int s = 0; s < sampleCount; s++)
            {
                int x = (int)((s + 0.5) * x1 / sampleCount);
                if (x >= image.Width)
                    x = image.Width - 1;
                int i = (y * image.Width + x) * 4;
                data[dest++] = image.Pixels[i];
                data[dest++] = image.Pixels[i + 1];
                data[dest++] = image.Pixels[i + 2];
            }
        }
        return data;
    }

    /// <summary>
    /// Mean per-channel error if <paramref name="next"/> is <paramref name="previous"/> scrolled up by <paramref name="delta"/> rows.
    /// </summary>
    private static double MeanShift(byte[] previous, byte[] next, int delta, int rows, int channels)
    {
        int compared = 0;
        long sum = 0;
        int step = Math.Max(1, (rows - delta) / 36);
        for (int y = delta; y < rows; y += step)
        {
            int a = y * channels;
            int b = (y - delta) * channels;
            for (int i = 0; i < channels; i++)
                sum += Math.Abs(previous[a + i] - next[b + i]);
            compared++;
        }
        if (compared == 0)
            return double.MaxValue;
        return sum / (double)(compared * channels);
    }
}
