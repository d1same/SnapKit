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
}
