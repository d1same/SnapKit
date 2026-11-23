using SnapKit;

namespace SnapKit.Tests;

public class StitcherTests
{
    [Fact]
    public void Detects_band_and_scroll_past_a_fixed_header()
    {
        var first = Frame(scroll: 0);
        var second = Frame(scroll: 10);

        var band = ImageStitcher.DetectChangedBand(first, second);
        Assert.False(band.IsEmpty);
        Assert.Equal(5, band.Top);
        Assert.Equal(40, band.Height);

        var match = ImageStitcher.FindScroll(first, second, band);
        Assert.True(match.Success);
        Assert.Equal(10, match.Delta);
        Assert.True(match.MeanError < 1, $"error was {match.MeanError}");
    }

    [Fact]
    public void Stitch_appends_only_new_rows()
    {
        var frames = new[]
        {
            Frame(scroll: 0),
            Frame(scroll: 10),
            Frame(scroll: 25)
        };
        var band = ImageStitcher.DetectChangedBand(frames[0], frames[1]);
        var second = ImageStitcher.FindScroll(frames[1], frames[2], band);
        Assert.True(second.Success, $"second delta={second.Delta} error={second.MeanError}");
        Assert.Equal(15, second.Delta);
        var stitched = ImageStitcher.StitchFrames(frames);

        // header 5 + content 40+25 + footer 4
        Assert.Equal(5 + 65 + 4, stitched.Height);
        Assert.Equal((byte)255, stitched.GetPixel(0, 0).B);
        Assert.Equal((byte)0, stitched.GetPixel(0, 0).R);

        var (b, _, _, _) = stitched.GetPixel(3, 5 + 64);
        Assert.Equal(ColorFor(64).B, b);

        var footer = stitched.GetPixel(1, stitched.Height - 1);
        Assert.Equal((byte)0, footer.B);
        Assert.Equal((byte)255, footer.R);
    }

    [Fact]
    public void Identical_frames_do_not_scroll()
    {
        var frame = Frame(scroll: 0);
        var band = ImageStitcher.DetectChangedBand(frame, frame.Clone());
        Assert.True(band.IsEmpty);
        var stitched = ImageStitcher.StitchFrames(new[] { frame, frame.Clone() });
        Assert.Equal(frame.Height, stitched.Height);
    }

    [Fact]
    public void Scrollbar_column_is_ignored()
    {
        var a = new PixelBuffer(30, 24);
        a.Fill(8, 8, 8);
        var b = a.Clone();
        for (int y = 0; y < b.Height; y++)
        {
            b.SetPixel(28, y, 255, 255, 255);
            b.SetPixel(29, y, 0, 0, 0);
        }

        var band = ImageStitcher.DetectChangedBand(a, b, ignoreRightPx: 4);
        Assert.True(band.IsEmpty);
    }

    [Fact]
    public void Sparse_webpage_with_wide_margins_and_paragraph_gaps_stitches_accurately()
    {
        // Simulate a 600x400 white webpage with a 40px static top navbar, wide white margins,
        // sparse dark "text lines" separated by 60px blank paragraph gaps, and white padding at bottom.
        var document = BuildSparseDocument(width: 600, height: 1100, textLeft: 180, textRight: 420);
        int headerH = 40;
        int viewportH = 360;

        var f0 = RenderWebFrame(document, headerH, viewportH, scrollY: 0);
        var f1 = RenderWebFrame(document, headerH, viewportH, scrollY: 140);
        var f2 = RenderWebFrame(document, headerH, viewportH, scrollY: 295);
        var f3 = RenderWebFrame(document, headerH, viewportH, scrollY: 322); // small end-of-page 27px scroll

        var band = ImageStitcher.DetectChangedBand(f0, f1, ignoreRightPx: 16);
        Assert.False(band.IsEmpty);
        Assert.True(band.Height > 240, $"band shattered across blank gaps: top={band.Top} bottom={band.Bottom} height={band.Height}");

        var m1 = ImageStitcher.FindScroll(f0, f1, band, ignoreRightPx: 16);
        Assert.True(m1.Success, $"m1 failed error={m1.MeanError}");
        Assert.Equal(140, m1.Delta);

        var m2 = ImageStitcher.FindScroll(f1, f2, band, ignoreRightPx: 16);
        Assert.True(m2.Success, $"m2 failed error={m2.MeanError}");
        Assert.Equal(155, m2.Delta);

        var m3 = ImageStitcher.FindScroll(f2, f3, band, ignoreRightPx: 16);
        Assert.True(m3.Success, $"m3 failed error={m3.MeanError}");
        Assert.Equal(27, m3.Delta);

        var stitched = ImageStitcher.StitchFrames(new[] { f0, f1, f2, f3 }, ignoreRightPx: 16);
        Assert.Equal(headerH + viewportH + 322, stitched.Height);
    }

    [Fact]
    public void Fixed_left_sidebar_and_floating_widget_do_not_break_matching()
    {
        var document = BuildSparseDocument(width: 640, height: 1000, textLeft: 210, textRight: 580);
        int headerH = 36;
        int viewportH = 320;

        PixelBuffer WithChrome(int scrollY)
        {
            var frame = RenderWebFrame(document, headerH, viewportH, scrollY);
            // Draw rich static left sidebar (0..180) with alternating colored items
            frame.FillRect(0, headerH, 180, viewportH, 30, 30, 30);
            for (int row = 0; row < viewportH; row += 24)
                frame.FillRect(16, headerH + row + 6, 130, 10, 90, 90, 95);
            // Draw static floating chat bubble in bottom-right corner
            frame.FillRect(560, headerH + viewportH - 44, 34, 34, 240, 240, 240);
            return frame;
        }

        var f0 = WithChrome(0);
        var f1 = WithChrome(110);
        var f2 = WithChrome(235);

        var band = ImageStitcher.DetectChangedBand(f0, f1, ignoreRightPx: 16);
        Assert.False(band.IsEmpty);
        Assert.True(band.Left >= 170, $"expected sidebar excluded, got band.Left={band.Left}");

        var m1 = ImageStitcher.FindScroll(f0, f1, band, ignoreRightPx: 16);
        Assert.True(m1.Success);
        Assert.Equal(110, m1.Delta);

        var m2 = ImageStitcher.FindScroll(f1, f2, band, ignoreRightPx: 16);
        Assert.True(m2.Success);
        Assert.Equal(125, m2.Delta);
    }

    [Fact]
    public void Late_appearing_sticky_header_is_skipped_cleanly()
    {
        var document = BuildSparseDocument(width: 500, height: 1000, textLeft: 60, textRight: 440);
        int headerH = 30;
        int viewportH = 300;

        var f0 = RenderWebFrame(document, headerH, viewportH, scrollY: 0);
        var f1 = RenderWebFrame(document, headerH, viewportH, scrollY: 100);
        var f2 = RenderWebFrame(document, headerH, viewportH, scrollY: 215);

        // Sticky bar appears at top of viewport AFTER scrolling starts (present in f1 and f2, absent in f0)
        f1.FillRect(0, headerH, 500, 36, 45, 45, 48);
        f2.FillRect(0, headerH, 500, 36, 45, 45, 48);

        var band = ImageStitcher.DetectChangedBand(f0, f1, ignoreRightPx: 12);
        var m2 = ImageStitcher.FindScroll(f1, f2, band, ignoreRightPx: 12);
        Assert.True(m2.Success, $"sticky header broke match: error={m2.MeanError}");
        Assert.Equal(115, m2.Delta);
    }

    private static PixelBuffer BuildSparseDocument(int width, int height, int textLeft, int textRight)
    {
        var doc = new PixelBuffer(width, height);
        doc.Fill(252, 252, 252);
        int y = 24;
        int paragraph = 0;
        while (y < height - 40)
        {
            // Draw 3 distinct "text" lines per paragraph with unique word lengths & subtle colors
            for (int line = 0; line < 3 && y < height - 20; line++)
            {
                int x = textLeft;
                int seed = paragraph * 31 + line * 17 + 5;
                while (x < textRight - 20)
                {
                    int wordW = 14 + (seed * 19) % 48;
                    seed = (seed * 1103515245 + 12345) & 0x7FFF;
                    if (x + wordW > textRight)
                        break;
                    byte ink = (byte)(25 + (seed % 50));
                    doc.FillRect(x, y, wordW, 3, ink, ink, ink);
                    x += wordW + 8;
                }
                y += 16;
            }
            // Large 56px blank whitespace gap between paragraphs
            y += 56;
            paragraph++;
        }
        return doc;
    }

    private static PixelBuffer RenderWebFrame(PixelBuffer doc, int headerH, int viewportH, int scrollY)
    {
        var frame = new PixelBuffer(doc.Width, headerH + viewportH);
        frame.FillRect(0, 0, doc.Width, headerH, 34, 34, 36);
        frame.CopyPixelsFrom(doc, 0, scrollY, 0, headerH, doc.Width, viewportH);
        return frame;
    }

    private static PixelBuffer Frame(int scroll)
    {
        const int width = 24;
        const int header = 5;
        const int viewport = 40;
        const int footer = 4;
        var image = new PixelBuffer(width, header + viewport + footer);
        image.FillRect(0, 0, width, header, 255, 0, 0);
        for (int i = 0; i < viewport; i++)
        {
            var color = ColorFor(scroll + i);
            image.FillRect(0, header + i, width, 1, color.B, color.G, color.R);
        }
        image.FillRect(0, header + viewport, width, footer, 0, 0, 255);
        return image;
    }

    private static (byte B, byte G, byte R) ColorFor(int index)
    {
        byte b = (byte)(16 + (index * 37) % 220);
        byte g = (byte)(16 + (index * 53) % 220);
        byte r = (byte)(16 + (index * 71) % 220);
        return (b, g, r);
    }
}
