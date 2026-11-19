namespace SnapKit.Tests;

public class OcrMapTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void Dpi_scaled_bitmap_lands_on_the_capture_pixels(double dpi)
    {
        var engine = new OcrBox(100 * dpi, 50 * dpi, 80 * dpi, 20 * dpi);
        int bitmapW = (int)Math.Round(400 * dpi);
        int bitmapH = (int)Math.Round(300 * dpi);

        var image = OcrMap.ToImagePixels(engine, bitmapW, bitmapH, 400, 300);

        Assert.Equal(100, image.X, 3);
        Assert.Equal(50, image.Y, 3);
        Assert.Equal(80, image.Width, 3);
        Assert.Equal(20, image.Height, 3);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void View_scale_keeps_the_word_on_the_same_fraction_of_the_picture(double dpi)
    {
        var word = new OcrBox(200, 80, 100, 24);
        double viewW = 1600 / dpi;
        double viewH = 900 / dpi;

        var view = OcrMap.ToView(word, 1600, 900, viewW, viewH, 0, 0);
        var back = OcrMap.FromView(view, 1600, 900, viewW, viewH, 0, 0);

        Assert.Equal(200 / dpi, view.X, 3);
        Assert.Equal(80 / dpi, view.Y, 3);
        Assert.Equal(100 / dpi, view.Width, 3);
        Assert.Equal(24 / dpi, view.Height, 3);
        Assert.Equal(200, back.X, 3);
        Assert.Equal(80, back.Y, 3);
        Assert.Equal(100, back.Width, 3);
        Assert.Equal(24, back.Height, 3);
    }

    [Fact]
    public void Virtual_screen_origin_moves_the_box_and_leaves_its_size_alone()
    {
        var word = new OcrBox(40, 18, 70, 16);
        double viewW = 800 / 1.25;
        double viewH = 600 / 1.25;

        var placed = OcrMap.ToView(word, 800, 600, viewW, viewH, 1920, -360);
        var back = OcrMap.FromView(placed, 800, 600, viewW, viewH, 1920, -360);

        Assert.Equal(1920 + 40 / 1.25, placed.X, 3);
        Assert.Equal(-360 + 18 / 1.25, placed.Y, 3);
        Assert.Equal(70 / 1.25, placed.Width, 3);
        Assert.Equal(16 / 1.25, placed.Height, 3);
        Assert.Equal(40, back.X, 3);
        Assert.Equal(18, back.Y, 3);
        Assert.Equal(70, back.Width, 3);
        Assert.Equal(16, back.Height, 3);
    }

    [Fact]
    public void Downscaled_recognition_bitmap_maps_back_before_the_view_scale()
    {
        var image = OcrMap.ToImagePixels(new OcrBox(50, 20, 40, 10), 400, 300, 800, 600);
        var view = OcrMap.ToView(image, 800, 600, 800 / 1.5, 600 / 1.5, 2560, 0);

        Assert.Equal(100, image.X, 3);
        Assert.Equal(40, image.Y, 3);
        Assert.Equal(2560 + 100 / 1.5, view.X, 3);
        Assert.Equal(40 / 1.5, view.Y, 3);
        Assert.Equal(80 / 1.5, view.Width, 3);
    }
}
