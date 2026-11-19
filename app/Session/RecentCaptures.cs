namespace SnapKit;

internal sealed class RecentCapture
{
    public RecentCapture(PixelBuffer image)
    {
        Image = image;
        Created = DateTime.Now;
    }

    public DateTime Created { get; }
    public PixelBuffer Image { get; set; }
    public string Label => $"{Created:h:mm:ss tt}   ·   {Image.Width}×{Image.Height}";
}

internal sealed class RecentCaptures
{
    public const int Max = 10;
    private readonly List<RecentCapture> _items = new();

    public IReadOnlyList<RecentCapture> Items => _items;

    public RecentCapture Add(PixelBuffer image)
    {
        var item = new RecentCapture(image);
        _items.Insert(0, item);
        while (_items.Count > Max)
            _items.RemoveAt(_items.Count - 1);
        return item;
    }

    public void Update(RecentCapture item, PixelBuffer image)
    {
        item.Image = image;
        _items.Remove(item);
        _items.Insert(0, item);
        while (_items.Count > Max)
            _items.RemoveAt(_items.Count - 1);
    }

    public void Clear() => _items.Clear();
}
