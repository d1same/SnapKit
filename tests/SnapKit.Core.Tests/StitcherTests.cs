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
