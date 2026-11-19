namespace SnapKit.Tests;

public class OcrTextTests
{
    [Fact]
    public void Whole_line_keeps_the_engine_text()
    {
        var words = new[]
        {
            new OcrPiece(0, "Hello,", "Hello, world", 2),
            new OcrPiece(0, "world", "Hello, world", 2)
        };

        Assert.Equal("Hello, world", OcrText.Join(words));
    }

    [Fact]
    public void Partial_line_joins_the_words_you_selected()
    {
        var words = new[]
        {
            new OcrPiece(0, "Hello,", "Hello, world", 2),
            new OcrPiece(1, "Save", "Save changes", 2)
        };

        Assert.Equal("Hello,\nSave", OcrText.Join(words));
    }

    [Fact]
    public void Selection_is_read_top_to_bottom_and_left_to_right()
    {
        var words = new[]
        {
            new OcrPiece(1, "changes", "Save changes", 3, X: 80, Y: 40),
            new OcrPiece(0, "world", "Hello, world", 2, X: 70, Y: 8),
            new OcrPiece(1, "Save", "Save changes", 3, X: 4, Y: 40),
            new OcrPiece(0, "Hello,", "Hello, world", 2, X: 6, Y: 8)
        };

        Assert.Equal("Hello, world\nSave changes", OcrText.Join(words));
    }

    [Fact]
    public void Partial_line_follows_the_words_from_left_to_right()
    {
        var words = new[]
        {
            new OcrPiece(0, "right", "left mid right", 3, X: 90, Y: 4),
            new OcrPiece(0, "left", "left mid right", 3, X: 2, Y: 4)
        };

        Assert.Equal("left right", OcrText.Join(words));
    }
}
