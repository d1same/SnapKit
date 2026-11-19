namespace SnapKit;

public sealed class ShotDocument
{
    private readonly List<Annotation> _annotations = new();
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private Snapshot? _pending;

    public PixelBuffer Source { get; }
    public IntRect View { get; private set; }
    public IReadOnlyList<Annotation> Annotations => _annotations;
    public int NextNumber { get; private set; } = 1;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public ShotDocument(PixelBuffer source)
    {
        Source = source;
        View = new IntRect(0, 0, source.Width, source.Height);
    }

    public void AddAnnotation(Annotation annotation)
    {
        Commit(() =>
        {
            if (annotation.Kind == AnnotationKind.Number)
            {
                if (annotation.Number <= 0)
                    annotation.Number = NextNumber++;
                else
                    NextNumber = Math.Max(NextNumber, annotation.Number + 1);
            }
            _annotations.Add(annotation);
        });
    }

    public void RemoveAnnotation(Guid id) => Commit(() => _annotations.RemoveAll(a => a.Id == id));

    public void UpdateText(Guid id, string text)
    {
        var annotation = _annotations.FirstOrDefault(a => a.Id == id);
        if (annotation == null || annotation.Text == text)
            return;
        Commit(() => annotation.Text = text);
    }

    public void SetView(IntRect view)
    {
        var full = new IntRect(0, 0, Source.Width, Source.Height);
        var next = full.Intersect(view);
        if (next.Width < 2 || next.Height < 2 || next.Equals(View))
            return;
        Commit(() => View = next);
    }

    public void BeginEdit() => _pending = Capture();

    public void EndEdit(bool changed)
    {
        if (_pending == null)
            return;
        if (!changed)
        {
            Restore(_pending);
            _pending = null;
            Changed?.Invoke();
            return;
        }
        _undo.Push(_pending);
        _pending = null;
        _redo.Clear();
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo)
            return;
        _redo.Push(Capture());
        Restore(_undo.Pop());
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo)
            return;
        _undo.Push(Capture());
        Restore(_redo.Pop());
        Changed?.Invoke();
    }

    public PixelBuffer ComposeUnderlay()
    {
        int w = Math.Max(1, View.Width);
        int h = Math.Max(1, View.Height);
        var image = Source.Crop(View.X, View.Y, w, h);
        foreach (var annotation in _annotations)
        {
            if (annotation.Kind != AnnotationKind.Blur || annotation.Points.Count < 2)
                continue;
            var (x, y, bw, bh) = ShapeMath.Bounds(annotation.Points);
            int ix = (int)Math.Round(x) - View.X;
            int iy = (int)Math.Round(y) - View.Y;
            int iw = Math.Max(1, (int)Math.Round(bw));
            int ih = Math.Max(1, (int)Math.Round(bh));
            int block = Math.Clamp((int)Math.Round(annotation.Stroke * 3), 6, 36);
            image.Pixelate(ix, iy, iw, ih, block);
        }
        return image;
    }

    public int BlurSignature()
    {
        var hash = new HashCode();
        hash.Add(View);
        foreach (var annotation in _annotations)
        {
            if (annotation.Kind != AnnotationKind.Blur)
                continue;
            hash.Add(annotation.Id);
            hash.Add(annotation.Stroke);
            foreach (var point in annotation.Points)
                hash.Add(point);
        }
        return hash.ToHashCode();
    }

    private void Commit(Action mutate)
    {
        _undo.Push(Capture());
        _redo.Clear();
        mutate();
        Changed?.Invoke();
    }

    private Snapshot Capture() => new()
    {
        View = View,
        Annotations = _annotations.Select(a => a.Clone()).ToList(),
        NextNumber = NextNumber
    };

    private void Restore(Snapshot snapshot)
    {
        View = snapshot.View;
        _annotations.Clear();
        _annotations.AddRange(snapshot.Annotations);
        NextNumber = snapshot.NextNumber;
    }

    private sealed class Snapshot
    {
        public IntRect View { get; init; }
        public List<Annotation> Annotations { get; init; } = new();
        public int NextNumber { get; init; }
    }
}
