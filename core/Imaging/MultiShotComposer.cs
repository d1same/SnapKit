namespace SnapKit;

public enum MultiShotLayout
{
    SideBySide,
    Vertical
}

public readonly record struct MultiShotSlot(int Index, IntRect ImageRect, int LabelY);

public sealed class MultiShotPlan
{
    public int TotalWidth { get; init; }
    public int TotalHeight { get; init; }
    public IReadOnlyList<MultiShotSlot> Slots { get; init; } = Array.Empty<MultiShotSlot>();
}

/// <summary>
/// Computes balanced side-by-side or vertical layouts for stitching 2–6 screenshots into one
/// clean Before/After or multi-step image.
/// </summary>
public static class MultiShotComposer
{
    public static MultiShotPlan Measure(
        IReadOnlyList<(int Width, int Height)> sizes,
        MultiShotLayout layout,
        int padding = 20,
        int gap = 16,
        int labelHeight = 0)
    {
        if (sizes.Count == 0)
            return new MultiShotPlan { TotalWidth = 1, TotalHeight = 1 };

        int pad = Math.Max(0, padding);
        int sp = Math.Max(0, gap);
        int lbl = Math.Max(0, labelHeight);
        var slots = new List<MultiShotSlot>(sizes.Count);

        if (layout == MultiShotLayout.SideBySide)
        {
            // Normalize heights gently toward median height (capped between 240 and 1400) so panels align neatly.
            int targetH = ClampTarget(Median(sizes.Select(s => Math.Max(1, s.Height))), 120, 1600);
            int x = pad;
            for (int i = 0; i < sizes.Count; i++)
            {
                int sw = Math.Max(1, sizes[i].Width);
                int sh = Math.Max(1, sizes[i].Height);
                double scale = Math.Min(1.0, targetH / (double)sh);
                if (sh < targetH && targetH / (double)sh <= 1.5)
                    scale = targetH / (double)sh;
                int w = Math.Max(1, (int)Math.Round(sw * scale));
                int h = Math.Max(1, (int)Math.Round(sh * scale));
                int y = pad + lbl + Math.Max(0, (targetH - h) / 2);
                slots.Add(new MultiShotSlot(i, new IntRect(x, y, w, h), pad));
                x += w + (i < sizes.Count - 1 ? sp : 0);
            }
            int maxBottom = slots.Max(s => s.ImageRect.Bottom);
            return new MultiShotPlan
            {
                TotalWidth = Math.Max(1, x + pad),
                TotalHeight = Math.Max(1, maxBottom + pad),
                Slots = slots
            };
        }
        else
        {
            // Vertical stack: cap width to largest width (up to 1800px) and center narrower shots.
            int maxW = ClampTarget(sizes.Max(s => Math.Max(1, s.Width)), 120, 1800);
            int y = pad;
            for (int i = 0; i < sizes.Count; i++)
            {
                int sw = Math.Max(1, sizes[i].Width);
                int sh = Math.Max(1, sizes[i].Height);
                double scale = sw > maxW ? maxW / (double)sw : 1.0;
                int w = Math.Max(1, (int)Math.Round(sw * scale));
                int h = Math.Max(1, (int)Math.Round(sh * scale));
                int x = pad + Math.Max(0, (maxW - w) / 2);
                int labelY = y;
                slots.Add(new MultiShotSlot(i, new IntRect(x, y + lbl, w, h), labelY));
                y += lbl + h + (i < sizes.Count - 1 ? sp : 0);
            }
            return new MultiShotPlan
            {
                TotalWidth = Math.Max(1, maxW + pad * 2),
                TotalHeight = Math.Max(1, y + pad),
                Slots = slots
            };
        }
    }

    private static int ClampTarget(int value, int min, int max) => Math.Clamp(value, min, max);

    private static int Median(IEnumerable<int> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0)
            return 400;
        int m = list.Count / 2;
        return list.Count % 2 == 1 ? list[m] : (list[m - 1] + list[m]) / 2;
    }
}
