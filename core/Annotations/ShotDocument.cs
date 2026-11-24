namespace SnapKit;

public sealed class ShotDocument
{
    private readonly List<Annotation> _annotations = new();
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private Snapshot? _pending;

    public PixelBuffer Source { get; }
    public IntRect View { get; private set; }
    public BackdropPreset Backdrop { get; private set; } = BackdropPreset.None;
    public bool ShowStamp { get; private set; }
    public string StampNote { get; private set; } = "";
    public IReadOnlyList<Annotation> Annotations => _annotations;
    public int NextNumber { get; private set; } = 1;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public ShotDocument(PixelBuffer source, IntRect? view = null, BackdropPreset backdrop = BackdropPreset.None, bool showStamp = false)
    {
        Source = source;
        View = view?.Clamp(source.Width, source.Height, 2) ?? new IntRect(0, 0, source.Width, source.Height);
        Backdrop = backdrop;
        ShowStamp = showStamp;
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

    public int AddAnnotations(IEnumerable<Annotation> annotations)
    {
        var list = annotations.ToList();
        if (list.Count == 0)
            return 0;
        Commit(() =>
        {
            foreach (var annotation in list)
            {
                if (annotation.Kind == AnnotationKind.Number)
                {
                    if (annotation.Number <= 0)
                        annotation.Number = NextNumber++;
                    else
                        NextNumber = Math.Max(NextNumber, annotation.Number + 1);
                }
                _annotations.Add(annotation);
            }
        });
        return list.Count;
    }

    public void RemoveAnnotation(Guid id) => Commit(() => _annotations.RemoveAll(a => a.Id == id));

    public bool ClearAnnotations()
    {
        if (_annotations.Count == 0 && NextNumber == 1)
            return false;
        Commit(() =>
        {
            _annotations.Clear();
            NextNumber = 1;
        });
        return true;
    }

    public bool ResetNumbering()
    {
        if (NextNumber == 1)
            return false;
        Commit(() => NextNumber = 1);
        return true;
    }

    public Annotation? DuplicateAnnotation(Guid id, double dx = 16, double dy = 16)
    {
        var original = _annotations.FirstOrDefault(item => item.Id == id);
        if (original == null)
            return null;
        var copy = original.Clone();
        copy.Id = Guid.NewGuid();
        for (int i = 0; i < copy.Points.Count; i++)
            copy.Points[i] = new PointD(copy.Points[i].X + dx, copy.Points[i].Y + dy);
        if (copy.Kind == AnnotationKind.Number)
            copy.Number = 0; // AddAnnotation assigns NextNumber++
        AddAnnotation(copy);
        return copy;
    }

    public bool NudgeAnnotation(Guid id, double dx, double dy)
    {
        if (Math.Abs(dx) < 0.001 && Math.Abs(dy) < 0.001)
            return false;
        var mark = _annotations.FirstOrDefault(item => item.Id == id);
        if (mark == null || mark.Points.Count == 0)
            return false;
        Commit(() =>
        {
            for (int i = 0; i < mark.Points.Count; i++)
                mark.Points[i] = new PointD(mark.Points[i].X + dx, mark.Points[i].Y + dy);
        });
        return true;
    }

    public bool SetBackdrop(BackdropPreset backdrop)
    {
        if (Backdrop == backdrop)
            return false;
        Commit(() => Backdrop = backdrop);
        return true;
    }

    public bool SetStamp(bool show, string? note = null)
    {
        string nextNote = note ?? StampNote;
        if (ShowStamp == show && string.Equals(StampNote, nextNote, StringComparison.Ordinal))
            return false;
        Commit(() =>
        {
            ShowStamp = show;
            StampNote = nextNote;
        });
        return true;
    }

    public bool Restyle(Guid id, string? colorHex, double? stroke, double? fontSize, ShapeFillMode? fillMode = null)
    {
        var mark = _annotations.FirstOrDefault(item => item.Id == id);
        if (mark == null)
            return false;

        bool useColor = colorHex != null && mark.Kind != AnnotationKind.Blur;
        bool useStroke = stroke != null && mark.Kind is AnnotationKind.Pen or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Rectangle or AnnotationKind.Ellipse or AnnotationKind.Highlighter or AnnotationKind.Blur or AnnotationKind.Magnifier;
        bool useFont = fontSize != null && mark.Kind is AnnotationKind.Text or AnnotationKind.Callout or AnnotationKind.Number;
        bool useFill = fillMode != null && mark.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse;
        string? nextColor = useColor ? HexColor.Normalize(colorHex, mark.ColorHex) : null;
        if (nextColor == mark.ColorHex)
            nextColor = null;
        double? nextStroke = useStroke ? Math.Clamp(stroke!.Value, 1, 16) : null;
        if (nextStroke != null && Math.Abs(nextStroke.Value - mark.Stroke) < 0.01)
            nextStroke = null;
        double? nextFont = useFont ? Math.Clamp(fontSize!.Value, 8, 72) : null;
        if (nextFont != null && Math.Abs(nextFont.Value - mark.FontSize) < 0.01)
            nextFont = null;
        ShapeFillMode? nextFill = useFill && fillMode!.Value != mark.FillMode ? fillMode.Value : null;
        if (nextColor == null && nextStroke == null && nextFont == null && nextFill == null)
            return false;

        Commit(() =>
        {
            if (nextColor != null)
                mark.ColorHex = nextColor;
            if (nextStroke != null)
                mark.Stroke = nextStroke.Value;
            if (nextFont != null)
                mark.FontSize = nextFont.Value;
            if (nextFill != null)
                mark.FillMode = nextFill.Value;
        });
        return true;
    }

    public void UpdateText(Guid id, string text)
    {
        var annotation = _annotations.FirstOrDefault(a => a.Id == id);
        if (annotation == null || annotation.Text == text)
            return;
        Commit(() => annotation.Text = text);
    }

    public void SetView(IntRect view)
    {
        var next = view.Clamp(Source.Width, Source.Height, 2);
        if (next.Equals(View))
            return;
        Commit(() => View = next);
    }

    public void PreviewView(IntRect view)
    {
        var next = view.Clamp(Source.Width, Source.Height, SelectionAdjust.MinSize);
        if (next.Equals(View))
            return;
        View = next;
        Changed?.Invoke();
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
        Backdrop = Backdrop,
        ShowStamp = ShowStamp,
        StampNote = StampNote,
        Annotations = _annotations.Select(a => a.Clone()).ToList(),
        NextNumber = NextNumber
    };

    private void Restore(Snapshot snapshot)
    {
        View = snapshot.View;
        Backdrop = snapshot.Backdrop;
        ShowStamp = snapshot.ShowStamp;
        StampNote = snapshot.StampNote;
        _annotations.Clear();
        _annotations.AddRange(snapshot.Annotations);
        NextNumber = snapshot.NextNumber;
    }

    private sealed class Snapshot
    {
        public IntRect View { get; init; }
        public BackdropPreset Backdrop { get; init; }
        public bool ShowStamp { get; init; }
        public string StampNote { get; init; } = "";
        public List<Annotation> Annotations { get; init; } = new();
        public int NextNumber { get; init; }
    }
}
