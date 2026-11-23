namespace SnapKit;

/// <summary>
/// A video annotation with a start/end time window and smooth fade-in/out envelope.
/// Coordinates are in the video's native pixel space (0..Width, 0..Height).
/// </summary>
public sealed class TimedAnnotation
{
    public const double DefaultDurationSeconds = 3.0;
    public const double MinDurationSeconds = 0.2;
    public const double DefaultFadeSeconds = 0.18;

    public required Annotation Annotation { get; init; }
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
    public double FadeSeconds { get; set; } = DefaultFadeSeconds;

    public Guid Id => Annotation.Id;
    public double DurationSeconds => Math.Max(0, EndSeconds - StartSeconds);

    public bool IsActiveAt(double timeSeconds) =>
        timeSeconds >= StartSeconds && timeSeconds <= EndSeconds;

    /// <summary>
    /// Returns opacity in [0, 1] at <paramref name="timeSeconds"/>, applying smooth ease-in/out.
    /// </summary>
    public double OpacityAt(double timeSeconds)
    {
        if (timeSeconds < StartSeconds || timeSeconds > EndSeconds)
            return 0;

        double span = EndSeconds - StartSeconds;
        if (span <= 0.001)
            return 1;

        double fade = Math.Clamp(FadeSeconds, 0, span / 3.0);
        if (fade <= 0.001)
            return 1;

        double fromStart = timeSeconds - StartSeconds;
        double toEnd = EndSeconds - timeSeconds;
        double edge = Math.Min(fromStart, toEnd);
        if (edge >= fade)
            return 1;

        double t = Math.Clamp(edge / fade, 0, 1);
        // Smoothstep ease
        return t * t * (3.0 - 2.0 * t);
    }

    public TimedAnnotation Clone() => new()
    {
        Annotation = Annotation.Clone(),
        StartSeconds = StartSeconds,
        EndSeconds = EndSeconds,
        FadeSeconds = FadeSeconds
    };
}

/// <summary>
/// Holds timed annotations and trim bounds for a recorded MP4 or GIF with full undo/redo.
/// </summary>
public sealed class VideoAnnotationTrack
{
    private readonly Stack<TrackSnapshot> _undo = new();
    private readonly Stack<TrackSnapshot> _redo = new();
    private List<TimedAnnotation> _items = new();

    public VideoAnnotationTrack(double durationSeconds)
    {
        DurationSeconds = Math.Max(TimedAnnotation.MinDurationSeconds, durationSeconds);
        TrimStartSeconds = 0;
        TrimEndSeconds = DurationSeconds;
    }

    public double DurationSeconds { get; }
    public double TrimStartSeconds { get; private set; }
    public double TrimEndSeconds { get; private set; }
    public int NextStepNumber { get; private set; } = 1;
    public IReadOnlyList<TimedAnnotation> Items => _items;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public bool HasEdits =>
        _items.Count > 0
        || TrimStartSeconds > 0.05
        || TrimEndSeconds < DurationSeconds - 0.05;

    public event Action? Changed;

    public TimedAnnotation Add(Annotation annotation, double startSeconds, double? durationSeconds = null)
    {
        double len = Math.Max(TimedAnnotation.MinDurationSeconds, durationSeconds ?? TimedAnnotation.DefaultDurationSeconds);
        double start = Math.Clamp(startSeconds, 0, Math.Max(0, DurationSeconds - TimedAnnotation.MinDurationSeconds));
        double end = Math.Clamp(start + len, start + TimedAnnotation.MinDurationSeconds, DurationSeconds);
        if (end - start < TimedAnnotation.MinDurationSeconds)
        {
            start = Math.Max(0, DurationSeconds - TimedAnnotation.MinDurationSeconds);
            end = DurationSeconds;
        }

        TimedAnnotation created = null!;
        Commit(() =>
        {
            var copy = annotation.Clone();
            if (copy.Kind == AnnotationKind.Number && copy.Number <= 0)
            {
                copy.Number = NextStepNumber;
                NextStepNumber++;
            }
            created = new TimedAnnotation
            {
                Annotation = copy,
                StartSeconds = start,
                EndSeconds = end,
                FadeSeconds = copy.Kind is AnnotationKind.Blur or AnnotationKind.Redact ? 0 : TimedAnnotation.DefaultFadeSeconds
            };
            _items.Add(created);
        });
        return created;
    }

    public TimedAnnotation? Find(Guid id) => _items.FirstOrDefault(i => i.Id == id);

    public bool Remove(Guid id)
    {
        int idx = _items.FindIndex(i => i.Id == id);
        if (idx < 0)
            return false;
        Commit(() => _items.RemoveAt(idx));
        return true;
    }

    public void Clear()
    {
        if (_items.Count == 0)
            return;
        Commit(() =>
        {
            _items.Clear();
            NextStepNumber = 1;
        });
    }

    public void ResetStepNumbers() => NextStepNumber = 1;

    public bool UpdateTiming(Guid id, double startSeconds, double endSeconds)
    {
        var item = Find(id);
        if (item == null)
            return false;
        double start = Math.Clamp(startSeconds, 0, Math.Max(0, DurationSeconds - TimedAnnotation.MinDurationSeconds));
        double end = Math.Clamp(endSeconds, start + TimedAnnotation.MinDurationSeconds, DurationSeconds);
        if (Math.Abs(item.StartSeconds - start) < 0.005 && Math.Abs(item.EndSeconds - end) < 0.005)
            return false;
        Commit(() =>
        {
            item.StartSeconds = start;
            item.EndSeconds = end;
        });
        return true;
    }

    public bool SetFullVideo(Guid id) => UpdateTiming(id, TrimStartSeconds, TrimEndSeconds);

    public bool SetTrim(double startSeconds, double endSeconds)
    {
        double start = Math.Clamp(startSeconds, 0, Math.Max(0, DurationSeconds - TimedAnnotation.MinDurationSeconds));
        double end = Math.Clamp(endSeconds, start + TimedAnnotation.MinDurationSeconds, DurationSeconds);
        if (Math.Abs(TrimStartSeconds - start) < 0.01 && Math.Abs(TrimEndSeconds - end) < 0.01)
            return false;
        Commit(() =>
        {
            TrimStartSeconds = start;
            TrimEndSeconds = end;
        });
        return true;
    }

    public bool MoveAnnotation(Guid id, double dx, double dy)
    {
        var item = Find(id);
        if (item == null || (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01))
            return false;
        Commit(() =>
        {
            for (int i = 0; i < item.Annotation.Points.Count; i++)
            {
                var p = item.Annotation.Points[i];
                item.Annotation.Points[i] = new PointD(p.X + dx, p.Y + dy);
            }
        });
        return true;
    }

    public bool ReplaceAnnotation(Guid id, Annotation updated)
    {
        var item = Find(id);
        if (item == null)
            return false;
        Commit(() =>
        {
            int idx = _items.FindIndex(i => i.Id == id);
            if (idx >= 0)
            {
                _items[idx] = new TimedAnnotation
                {
                    Annotation = updated.Clone(),
                    StartSeconds = item.StartSeconds,
                    EndSeconds = item.EndSeconds,
                    FadeSeconds = item.FadeSeconds
                };
            }
        });
        return true;
    }

    public bool Restyle(Guid id, string? colorHex = null, double? stroke = null, double? fontSize = null, ShapeFillMode? fillMode = null)
    {
        var item = Find(id);
        if (item == null)
            return false;
        var mark = item.Annotation;
        string? nextColor = colorHex != null && mark.Kind != AnnotationKind.Blur ? HexColor.Normalize(colorHex, mark.ColorHex) : null;
        if (nextColor == mark.ColorHex)
            nextColor = null;
        double? nextStroke = stroke != null ? Math.Clamp(stroke.Value, 1, 16) : null;
        if (nextStroke != null && Math.Abs(nextStroke.Value - mark.Stroke) < 0.01)
            nextStroke = null;
        double? nextFont = fontSize != null ? Math.Clamp(fontSize.Value, 8, 72) : null;
        if (nextFont != null && Math.Abs(nextFont.Value - mark.FontSize) < 0.01)
            nextFont = null;
        ShapeFillMode? nextFill = fillMode != null && mark.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse && fillMode.Value != mark.FillMode
            ? fillMode.Value
            : null;
        if (nextColor == null && nextStroke == null && nextFont == null && nextFill == null)
            return false;

        Commit(() =>
        {
            if (nextColor != null) mark.ColorHex = nextColor;
            if (nextStroke != null) mark.Stroke = nextStroke.Value;
            if (nextFont != null) mark.FontSize = nextFont.Value;
            if (nextFill != null) mark.FillMode = nextFill.Value;
        });
        return true;
    }

    public bool Undo()
    {
        if (_undo.Count == 0)
            return false;
        _redo.Push(Capture());
        Restore(_undo.Pop());
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_redo.Count == 0)
            return false;
        _undo.Push(Capture());
        Restore(_redo.Pop());
        Changed?.Invoke();
        return true;
    }

    public IReadOnlyList<(Annotation Annotation, double Opacity)> ActiveAt(double timeSeconds)
    {
        var active = new List<(Annotation, double)>();
        foreach (var item in _items)
        {
            double op = item.OpacityAt(timeSeconds);
            if (op > 0.004)
                active.Add((item.Annotation, op));
        }
        return active;
    }

    /// <summary>
    /// Computes a quantized cache key for the active overlay at <paramref name="timeSeconds"/>
    /// so video export can reuse the rendered vector overlay across frames when opacity is constant.
    /// </summary>
    public long OverlayCacheKeyAt(double timeSeconds)
    {
        var hash = new HashCode();
        int count = 0;
        foreach (var item in _items)
        {
            if (item.Annotation.Kind == AnnotationKind.Blur)
                continue;
            double op = item.OpacityAt(timeSeconds);
            if (op <= 0.004)
                continue;
            int qAlpha = (int)Math.Round(op * 64);
            hash.Add(item.Id);
            hash.Add(qAlpha);
            count++;
        }
        if (count == 0)
            return 0;
        long h = hash.ToHashCode();
        return h == 0 ? 1 : h;
    }

    public static string ShortLabel(Annotation annotation) => annotation.Kind switch
    {
        AnnotationKind.Arrow => "Arrow",
        AnnotationKind.Pen => "Pen",
        AnnotationKind.Line => "Line",
        AnnotationKind.Rectangle => "Box",
        AnnotationKind.Ellipse => "Circle",
        AnnotationKind.Highlighter => "Highlight",
        AnnotationKind.Text => string.IsNullOrWhiteSpace(annotation.Text) ? "Text" : Truncate(annotation.Text, 14),
        AnnotationKind.Callout => string.IsNullOrWhiteSpace(annotation.Text) ? "Callout" : Truncate(annotation.Text, 14),
        AnnotationKind.Number => $"Step {annotation.Number}",
        AnnotationKind.Blur => "Blur",
        AnnotationKind.Redact => "Redact",
        AnnotationKind.Spotlight => "Spotlight",
        _ => annotation.Kind.ToString()
    };

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private void Commit(Action mutate)
    {
        _undo.Push(Capture());
        _redo.Clear();
        mutate();
        Changed?.Invoke();
    }

    private TrackSnapshot Capture() => new()
    {
        Items = _items.Select(i => i.Clone()).ToList(),
        TrimStart = TrimStartSeconds,
        TrimEnd = TrimEndSeconds,
        NextStep = NextStepNumber
    };

    private void Restore(TrackSnapshot snap)
    {
        _items = snap.Items.Select(i => i.Clone()).ToList();
        TrimStartSeconds = snap.TrimStart;
        TrimEndSeconds = snap.TrimEnd;
        NextStepNumber = snap.NextStep;
    }

    private sealed class TrackSnapshot
    {
        public required List<TimedAnnotation> Items { get; init; }
        public double TrimStart { get; init; }
        public double TrimEnd { get; init; }
        public int NextStep { get; init; }
    }
}
