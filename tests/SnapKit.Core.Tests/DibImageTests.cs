namespace SnapKit.Tests;

public class DibImageTests
{
    [Fact]
    public void Packs_bottom_up_bgr_and_pads_rows()
    {
        // Two pixels wide so the 24-bit row needs one pad byte.
        byte[] bgra =
        {
            10, 20, 30, 255, 40, 50, 60, 255,
            1, 2, 3, 255, 4, 5, 6, 255
        };

        var dib = DibImage.FromTopDownBgra(bgra, 2, 2, 8);

        Assert.Equal(40, dib[0]);
        Assert.Equal(2, dib[4]);
        Assert.Equal(2, dib[8]);
        Assert.Equal(24, dib[14]);
        Assert.Equal(1, dib[40]);
        Assert.Equal(2, dib[41]);
        Assert.Equal(3, dib[42]);
        Assert.Equal(4, dib[43]);
        Assert.Equal(5, dib[44]);
        Assert.Equal(6, dib[45]);
        Assert.Equal(0, dib[46]);
        Assert.Equal(10, dib[48]);
        Assert.Equal(20, dib[49]);
        Assert.Equal(30, dib[50]);
    }
}
