namespace SnapKit;

/// <summary>
/// Stitches scrolled window frames into one tall image.
/// Scrolling down moves existing content up, so the bottom of the previous
/// viewport matches the top of the next one. Fixed headers and footers are
/// the rows that do not change between frames.
/// </summary>
public static class ImageStitcher
{
    public readonly record struct ChangeBand(int Top, int Bottom, int Left = 0, int Right = 0)
    {
        public int Height => Math.Max(0, Bottom - Top);
        public int Width => Math.Max(0, Right - Left);
        public bool IsEmpty => Height < 8;
        public static ChangeBand Empty => new(0, 0);
    }

    public readonly record struct ScrollMatch(bool Success, int Delta, double MeanError);

    /// <summary>
    /// Fraction (0..1) of pixels inside the region that changed noticeably between two same-sized frames.
    /// Reliable even on sparse text pages with mostly solid backgrounds.
    /// </summary>
    public static double ChangedPixelRatio(PixelBuffer a, PixelBuffer b, ChangeBand? band = null, int ignoreRightPx = 0, int channelSumThreshold = 30)
    {
        if (a.Width != b.Width || a.Height != b.Height || a.Width < 4 || a.Height < 4)
            return 1.0;

        int xEnd = EffectiveRight(a.Width, ignoreRightPx);
        int xStart = 0;
        int yStart = 0;
        int yEnd = a.Height;
        if (band is { IsEmpty: false } bnd)
        {
            yStart = Math.Clamp(bnd.Top, 0, a.Height);
            yEnd = Math.Clamp(bnd.Bottom, yStart, a.Height);
            if (bnd.Right > bnd.Left)
            {
                xStart = Math.Clamp(bnd.Left, 0, xEnd);
                xEnd = Math.Clamp(bnd.Right, xStart, xEnd);
            }
        }
        if (xEnd <= xStart || yEnd <= yStart)
            return 0.0;

        int stepX = xEnd - xStart > 240 ? 2 : 1;
        int stepY = yEnd - yStart > 240 ? 2 : 1;
        int changed = 0;
        int total = 0;
        for (int y = yStart; y < yEnd; y += stepY)
        {
            int rowA = y * a.Stride;
            int rowB = y * b.Stride;
            for (int x = xStart; x < xEnd; x += stepX)
            {
                int ia = rowA + x * 4;
                int ib = rowB + x * 4;
                int diff = Math.Abs(a.Pixels[ia] - b.Pixels[ib])
                    + Math.Abs(a.Pixels[ia + 1] - b.Pixels[ib + 1])
                    + Math.Abs(a.Pixels[ia + 2] - b.Pixels[ib + 2]);
                if (diff >= channelSumThreshold)
                    changed++;
                total++;
            }
        }
        return total == 0 ? 0.0 : changed / (double)total;
    }

    public static ChangeBand DetectChangedBand(PixelBuffer previous, PixelBuffer next, int ignoreRightPx = 0, int threshold = 12)
    {
        if (previous.Width != next.Width || previous.Height != next.Height)
            throw new ArgumentException("Frames must be the same size.");

        int height = previous.Height;
        int xLimit = EffectiveRight(previous.Width, ignoreRightPx);
        var changed = new bool[height];
        for (int y = 0; y < height; y++)
            changed[y] = RowChanged(previous, next, y, 0, xLimit, threshold);

        // Bridge 1-row gaps (e.g. anti-aliased horizontal lines) before clustering.
        for (int y = 1; y < height - 1; y++)
        {
            if (!changed[y] && changed[y - 1] && changed[y + 1])
                changed[y] = true;
        }

        // Collect contiguous changed row runs instead of keeping only the single longest
        // gapless run — real pages have blank margins between paragraphs/cards that are
        // unchanged whitespace inside the scrollable viewport.
        var runs = new List<(int Start, int End)>();
        int runStart = -1;
        for (int y = 0; y <= height; y++)
        {
            bool on = y < height && changed[y];
            if (on)
            {
                if (runStart < 0)
                    runStart = y;
            }
            else if (runStart >= 0)
            {
                runs.Add((runStart, y));
                runStart = -1;
            }
        }

        if (runs.Count == 0)
            return ChangeBand.Empty;

        // Filter out isolated micro-noise specks (e.g., 1-3px cursor blink or tab hover highlight
        // separated by a wide static gap from actual scrolling content).
        int totalChanged = 0;
        foreach (var r in runs)
            totalChanged += r.End - r.Start;
        if (totalChanged < 4)
            return ChangeBand.Empty;

        var kept = new List<(int Start, int End)>(runs.Count);
        for (int i = 0; i < runs.Count; i++)
        {
            var r = runs[i];
            int len = r.End - r.Start;
            if (len >= 4)
            {
                kept.Add(r);
                continue;
            }
            bool nearPrev = i > 0 && (r.Start - runs[i - 1].End) <= 24;
            bool nearNext = i + 1 < runs.Count && (runs[i + 1].Start - r.End) <= 24;
            if (nearPrev || nearNext)
                kept.Add(r);
        }

        if (kept.Count == 0)
            kept.AddRange(runs);

        int top = kept[0].Start;
        int bottom = kept[^1].End;
        if (bottom - top < 8)
            return ChangeBand.Empty;

        var (xLeft, xRight) = DetectActiveColumns(previous, next, top, bottom, xLimit, threshold);
        return new ChangeBand(top, bottom, xLeft, xRight);
    }

    public static ScrollMatch FindScroll(PixelBuffer previous, PixelBuffer next, ChangeBand band, int ignoreRightPx = 0)
    {
        int rows = band.Height;
        if (rows < 8 || previous.Width != next.Width || previous.Height != next.Height)
            return new ScrollMatch(false, 0, double.MaxValue);

        int xLimit = EffectiveRight(previous.Width, ignoreRightPx);

        // Skip rows at the top of the band that became static between previous and next
        // (e.g. a sticky header that appeared after scroll started or blank top padding).
        int maxSkipTop = Math.Max(0, rows - Math.Max(16, rows / 2));
        int skipTop = 0;
        while (skipTop < maxSkipTop && !RowChanged(previous, next, band.Top + skipTop, 0, xLimit, threshold: 10))
            skipTop++;

        int effectiveRows = rows - skipTop;
        int minOverlap = effectiveRows < 32
            ? Math.Max(4, effectiveRows / 4)
            : Math.Clamp(effectiveRows / 6, 16, 96);
        int maxDelta = effectiveRows - minOverlap;
        if (maxDelta < 1)
            return new ScrollMatch(false, 0, double.MaxValue);

        // Restrict matching horizontally to the active changing columns so static sidebars
        // (VS Code, Slack, ChatGPT, docs nav) and blank outer margins never bias the match.
        var (x0, x1) = DetectActiveColumns(previous, next, band.Top + skipTop, band.Bottom, xLimit, threshold: 10);
        if (x1 - x0 < Math.Min(16, xLimit))
        {
            x0 = band.Right > band.Left ? Math.Clamp(band.Left, 0, xLimit) : 0;
            x1 = band.Right > band.Left ? Math.Clamp(band.Right, x0, xLimit) : xLimit;
            if (x1 - x0 < Math.Min(16, xLimit))
            {
                x0 = 0;
                x1 = xLimit;
            }
        }

        int sampleCount = Math.Clamp((x1 - x0) / 3, Math.Min(8, x1 - x0), 160);
        if (sampleCount < 4)
            sampleCount = Math.Max(1, x1 - x0);

        var prev = SampleBand(previous, band.Top, rows, x0, x1, sampleCount);
        var nextS = SampleBand(next, band.Top, rows, x0, x1, sampleCount);
        int channels = sampleCount * 3;

        var (identical, changedFrac) = MeasureUnshifted(prev, nextS, skipTop, rows, channels);
        if (identical < 0.6 && changedFrac < 0.003)
            return new ScrollMatch(false, 0, identical);

        // Precompute vertical texture (row-to-row contrast) prefix sums on `prev` so we can
        // immediately reject degenerate blank-margin-on-blank-margin false matches.
        var contrastPrefix = ComputeRowContrastPrefix(prev, rows, channels);
        double viewportTexture = (contrastPrefix[rows] - contrastPrefix[skipTop]) / Math.Max(1, effectiveRows);

        // Stage 1: Fast scan across EVERY single delta (1..maxDelta) using stride-sampled columns & rows.
        int coarseColStep = Math.Max(1, sampleCount / 40);
        var topCandidates = new List<(int Delta, double Score)>(16);
        int bestRawDelta = 0;
        double bestRawCoarse = double.MaxValue;

        for (int delta = 1; delta <= maxDelta; delta++)
        {
            int overlap = rows - (delta + skipTop);
            double overlapTexture = (contrastPrefix[rows] - contrastPrefix[delta + skipTop]) / Math.Max(1, overlap);

            // Reject overlaps that contain almost no visual structure when the viewport itself has content.
            if (viewportTexture >= 1.5 && overlapTexture < 0.45)
                continue;

            double coarseMae = MeanShiftCapped(prev, nextS, delta, skipTop, rows, sampleCount, coarseColStep, maxRows: 48, capPerChannel: 48);
            double normScore = coarseMae / Math.Max(3.0, Math.Min(overlapTexture, 24.0));

            if (coarseMae < bestRawCoarse && (viewportTexture < 1.5 || overlapTexture >= 0.75))
            {
                bestRawCoarse = coarseMae;
                bestRawDelta = delta;
            }

            InsertTopCandidate(topCandidates, delta, normScore, limit: 12);
        }

        if (bestRawDelta > 0)
            InsertTopCandidate(topCandidates, bestRawDelta, -1, limit: 13);

        if (topCandidates.Count == 0)
            return new ScrollMatch(false, 0, double.MaxValue);

        // Stage 2: Fine full-column verification on top candidates and their +/-1 row neighbors.
        var tested = new HashSet<int>();
        int bestDelta = 0;
        double bestFineScore = double.MaxValue;
        double bestCappedMae = double.MaxValue;
        double bestRawMae = double.MaxValue;

        foreach (var cand in topCandidates)
        {
            for (int d = Math.Max(1, cand.Delta - 1); d <= Math.Min(maxDelta, cand.Delta + 1); d++)
            {
                if (!tested.Add(d))
                    continue;

                int overlap = rows - (d + skipTop);
                double overlapTexture = (contrastPrefix[rows] - contrastPrefix[d + skipTop]) / Math.Max(1, overlap);
                if (viewportTexture >= 1.5 && overlapTexture < 0.45)
                    continue;

                var (rawMae, cappedMae) = MeanShiftDetailed(prev, nextS, d, skipTop, rows, channels, maxRows: 220, capPerChannel: 48);
                double fineScore = cappedMae / Math.Max(3.0, Math.Min(overlapTexture, 24.0));

                bool better = fineScore < bestFineScore - 0.015
                    || (Math.Abs(fineScore - bestFineScore) <= 0.015 && cappedMae < bestCappedMae - 0.3)
                    || (Math.Abs(fineScore - bestFineScore) <= 0.015 && Math.Abs(cappedMae - bestCappedMae) <= 0.3 && d < bestDelta);

                if (better)
                {
                    bestFineScore = fineScore;
                    bestCappedMae = cappedMae;
                    bestRawMae = rawMae;
                    bestDelta = d;
                }
            }
        }

        if (bestDelta <= 0 || bestCappedMae > 24.0)
            return new ScrollMatch(false, 0, bestRawMae);

        // Must be a meaningfully better alignment than unshifted frames.
        if (bestCappedMae > identical * 0.75 && bestCappedMae > 1.5)
            return new ScrollMatch(false, 0, bestRawMae);

        return new ScrollMatch(true, bestDelta, bestRawMae);
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

    /// <summary>Stitch a sequence of same-sized frames. Used by tests and offline stitching.</summary>
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

        // Take footer from the final stitched frame so end-of-page content that scrolled into
        // originally-blank bottom rows is preserved cleanly (and fixed footers remain identical).
        PixelBuffer? footer = footerHeight > 0 ? previous.Crop(0, band.Bottom, previous.Width, footerHeight) : null;
        return Compose(header, content, footer);
    }

    private static int EffectiveRight(int width, int ignoreRightPx)
    {
        int x1 = width - Math.Max(0, ignoreRightPx);
        return x1 < 4 ? width : x1;
    }

    private static bool RowChanged(PixelBuffer a, PixelBuffer b, int y, int x0, int x1, int threshold)
    {
        int width = x1 - x0;
        if (width <= 0)
            return false;
        int step = width > 320 ? 2 : 1;
        int sampled = (width + step - 1) / step;
        int minChanged = Math.Max(2, sampled / 200);
        int channelSumLimit = threshold * 3;
        int row = y * a.Stride;
        int changed = 0;

        for (int x = x0; x < x1; x += step)
        {
            int i = row + x * 4;
            int diff = Math.Abs(a.Pixels[i] - b.Pixels[i])
                + Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1])
                + Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]);
            if (diff > channelSumLimit)
            {
                changed++;
                if (changed >= minChanged)
                    return true;
            }
        }
        return false;
    }

    private static (int Left, int Right) DetectActiveColumns(PixelBuffer a, PixelBuffer b, int yTop, int yBottom, int xLimit, int threshold)
    {
        int height = yBottom - yTop;
        if (height <= 0 || xLimit <= 8)
            return (0, xLimit);

        int colStep = xLimit > 480 ? 4 : (xLimit > 160 ? 2 : 1);
        int rowStep = Math.Max(1, height / 96);
        int channelSumLimit = threshold * 3;
        int minRowsChanged = Math.Max(2, (height / rowStep) / 40);

        int firstCol = -1;
        int lastCol = -1;

        for (int x = 0; x < xLimit; x += colStep)
        {
            int changedRows = 0;
            int byteOffset = x * 4;
            for (int y = yTop; y < yBottom; y += rowStep)
            {
                int i = y * a.Stride + byteOffset;
                int diff = Math.Abs(a.Pixels[i] - b.Pixels[i])
                    + Math.Abs(a.Pixels[i + 1] - b.Pixels[i + 1])
                    + Math.Abs(a.Pixels[i + 2] - b.Pixels[i + 2]);
                if (diff > channelSumLimit)
                {
                    changedRows++;
                    if (changedRows >= minRowsChanged)
                    {
                        if (firstCol < 0)
                            firstCol = x;
                        lastCol = x;
                        break;
                    }
                }
            }
        }

        if (firstCol < 0 || lastCol <= firstCol)
            return (0, xLimit);

        int left = Math.Max(0, firstCol - colStep);
        int right = Math.Min(xLimit, lastCol + colStep + 1);
        return (left, right);
    }

    private static byte[] SampleBand(PixelBuffer image, int bandTop, int rows, int x0, int x1, int sampleCount)
    {
        int span = Math.Max(1, x1 - x0);
        int channels = sampleCount * 3;
        var data = new byte[rows * channels];
        for (int row = 0; row < rows; row++)
        {
            int y = bandTop + row;
            int srcRow = y * image.Stride;
            int dest = row * channels;
            for (int s = 0; s < sampleCount; s++)
            {
                int x = x0 + (int)((s + 0.5) * span / sampleCount);
                if (x >= image.Width)
                    x = image.Width - 1;
                int i = srcRow + x * 4;
                data[dest++] = image.Pixels[i];
                data[dest++] = image.Pixels[i + 1];
                data[dest++] = image.Pixels[i + 2];
            }
        }
        return data;
    }

    private static (double MeanError, double ChangedFraction) MeasureUnshifted(byte[] prev, byte[] next, int skipTop, int rows, int channels)
    {
        int step = Math.Max(1, (rows - skipTop) / 120);
        long sum = 0;
        int samples = 0;
        int changedPixels = 0;
        int totalPixels = 0;

        for (int y = skipTop; y < rows; y += step)
        {
            int offset = y * channels;
            for (int i = 0; i < channels; i += 3)
            {
                int d = Math.Abs(prev[offset + i] - next[offset + i])
                    + Math.Abs(prev[offset + i + 1] - next[offset + i + 1])
                    + Math.Abs(prev[offset + i + 2] - next[offset + i + 2]);
                sum += d;
                if (d >= 24)
                    changedPixels++;
                totalPixels++;
                samples += 3;
            }
        }

        double mean = samples == 0 ? 0 : sum / (double)samples;
        double frac = totalPixels == 0 ? 0 : changedPixels / (double)totalPixels;
        return (mean, frac);
    }

    private static double[] ComputeRowContrastPrefix(byte[] prev, int rows, int channels)
    {
        var prefix = new double[rows + 1];
        for (int y = 0; y < rows; y++)
        {
            double rowContrast = 0;
            if (y > 0)
            {
                int cur = y * channels;
                int up = (y - 1) * channels;
                long sum = 0;
                for (int i = 0; i < channels; i++)
                    sum += Math.Abs(prev[cur + i] - prev[up + i]);
                rowContrast = sum / (double)channels;
            }
            prefix[y + 1] = prefix[y] + rowContrast;
        }
        return prefix;
    }

    private static double MeanShiftCapped(byte[] previous, byte[] next, int delta, int skipTop, int rows, int sampleCount, int colStep, int maxRows, int capPerChannel)
    {
        int channels = sampleCount * 3;
        int overlap = rows - (delta + skipTop);
        if (overlap <= 0)
            return double.MaxValue;

        int rowStep = Math.Max(1, overlap / maxRows);
        int channelStep = colStep * 3;
        long sum = 0;
        int count = 0;

        for (int y = delta + skipTop; y < rows; y += rowStep)
        {
            int a = y * channels;
            int b = (y - delta) * channels;
            for (int i = 0; i < channels; i += channelStep)
            {
                int d0 = Math.Abs(previous[a + i] - next[b + i]);
                int d1 = Math.Abs(previous[a + i + 1] - next[b + i + 1]);
                int d2 = Math.Abs(previous[a + i + 2] - next[b + i + 2]);
                sum += Math.Min(d0, capPerChannel) + Math.Min(d1, capPerChannel) + Math.Min(d2, capPerChannel);
                count += 3;
            }
        }

        return count == 0 ? double.MaxValue : sum / (double)count;
    }

    private static (double RawMae, double CappedMae) MeanShiftDetailed(byte[] previous, byte[] next, int delta, int skipTop, int rows, int channels, int maxRows, int capPerChannel)
    {
        int overlap = rows - (delta + skipTop);
        if (overlap <= 0)
            return (double.MaxValue, double.MaxValue);

        int rowStep = Math.Max(1, overlap / maxRows);
        long rawSum = 0;
        long capSum = 0;
        int compared = 0;

        for (int y = delta + skipTop; y < rows; y += rowStep)
        {
            int a = y * channels;
            int b = (y - delta) * channels;
            for (int i = 0; i < channels; i++)
            {
                int d = Math.Abs(previous[a + i] - next[b + i]);
                rawSum += d;
                capSum += d < capPerChannel ? d : capPerChannel;
            }
            compared++;
        }

        if (compared == 0)
            return (double.MaxValue, double.MaxValue);
        double denom = compared * (double)channels;
        return (rawSum / denom, capSum / denom);
    }

    private static void InsertTopCandidate(List<(int Delta, double Score)> list, int delta, double score, int limit)
    {
        if (list.Count >= limit && score >= list[^1].Score)
            return;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Delta == delta)
            {
                if (score < list[i].Score)
                {
                    list.RemoveAt(i);
                    break;
                }
                return;
            }
        }
        int insertAt = list.Count;
        while (insertAt > 0 && score < list[insertAt - 1].Score)
            insertAt--;
        list.Insert(insertAt, (delta, score));
        if (list.Count > limit)
            list.RemoveAt(list.Count - 1);
    }
}
