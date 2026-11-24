namespace SnapKit;

internal sealed class OcrToken
{
    public string Text { get; init; } = "";
    public int Line { get; init; }
    public string LineText { get; init; } = "";
    public int WordsInLine { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Hits(double x, double y, double pad = 0)
    {
        return x >= X - pad && y >= Y - pad && x < Right + pad && y < Bottom + pad;
    }

    public bool Intersects(double x, double y, double width, double height)
    {
        double right = x + width;
        double bottom = y + height;
        return X < right && Right > x && Y < bottom && Bottom > y;
    }

    public OcrPiece Piece => new(Line, Text, LineText, WordsInLine, X, Y);
    public OcrWordBox WordBox => new(Text, X, Y, Width, Height);
}

internal sealed class OcrPage
{
    public string Text { get; init; } = "";
    public IReadOnlyList<OcrToken> Words { get; init; } = Array.Empty<OcrToken>();

    public string TextFor(IEnumerable<int> indexes)
    {
        var pieces = indexes
            .Where(index => index >= 0 && index < Words.Count)
            .Distinct()
            .OrderBy(index => index)
            .Select(index => Words[index].Piece)
            .ToList();
        return OcrText.Join(pieces);
    }

    public OcrTableResult TableFor(IEnumerable<int> indexes)
    {
        var boxes = indexes
            .Where(index => index >= 0 && index < Words.Count)
            .Distinct()
            .Select(index => Words[index].WordBox)
            .ToList();
        return OcrTable.Build(boxes);
    }

    public OcrTableResult TableAll()
    {
        var boxes = new OcrWordBox[Words.Count];
        for (int i = 0; i < Words.Count; i++)
            boxes[i] = Words[i].WordBox;
        return OcrTable.Build(boxes);
    }
}
