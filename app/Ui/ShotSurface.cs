using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapKit;

internal sealed class ShotSurface : Grid
{
    private readonly Image _image = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly PaintLayer _paint = new() { IsHitTestVisible = false };
    private readonly Canvas _textLayer = new();
    private TextBox? _box;
    private Annotation? _draft;
    private Annotation? _editing;
    private Annotation? _moving;
    private List<PointD>? _moveFrom;
    private PointD _press;
    private bool _editIsNew;
    private bool _cropping;
    private bool _finishing;
    private string _editOriginal = "";
    private bool _suppress;
    private int _blurSig = int.MinValue;
    private Guid? _selected;

    public ShotSurface(ShotDocument document)
    {
        Document = document;
        Background = Brushes.Transparent;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Children.Add(_image);
        Children.Add(_paint);
        Children.Add(_textLayer);
        _paint.Paint = Paint;
        PixelScale = 1;
        Width = document.View.Width;
        Height = document.View.Height;
        RefreshImage(force: true);
        document.Changed += () => Dispatcher.Invoke(OnDocumentChanged);
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) => FinishDrag(cancel: false);
    }

    public ShotDocument Document { get; }
    public double PixelScale { get; private set; }
    public EditorTool Tool { get; set; } = EditorTool.Arrow;
    public string ColorHex { get; set; } = "#FF3B30";
    public double StrokeWidth { get; set; } = 3;
    public double FontSize { get; set; } = 18;
    public bool IsEditingText => _editing != null;
    public event Action<string>? ColorPicked;

    public void SetPixelScale(double scale)
    {
        PixelScale = Math.Max(0.05, scale);
        OnDocumentChanged();
    }

    public void CancelText()
    {
        if (_editing == null)
            return;
        _suppress = true;
        _editing = null;
        if (_box != null)
        {
            _box.Visibility = Visibility.Collapsed;
            _box.Text = "";
        }
        _suppress = false;
        _paint.InvalidateVisual();
    }

    public void DeleteSelection()
    {
        if (_selected == null)
            return;
        var id = _selected.Value;
        _selected = null;
        Document.RemoveAnnotation(id);
    }

    private void OnDocumentChanged()
    {
        Width = Math.Max(1, Document.View.Width / PixelScale);
        Height = Math.Max(1, Document.View.Height / PixelScale);
        RefreshImage(force: false);
        PositionTextBox();
        _paint.InvalidateVisual();
    }

    private void RefreshImage(bool force)
    {
        int signature = Document.BlurSignature();
        if (!force && signature == _blurSig)
            return;
        _blurSig = signature;
        _image.Source = BitmapConvert.ToBitmapSource(Document.ComposeUnderlay());
        RenderOptions.SetBitmapScalingMode(_image, PixelScale >= 0.99 && PixelScale <= 1.01
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.HighQuality);
    }

    private void Paint(DrawingContext dc)
    {
        double zoom = 1.0 / PixelScale;
        dc.PushTransform(new ScaleTransform(zoom, zoom));
        dc.PushTransform(new TranslateTransform(-Document.View.X, -Document.View.Y));
        foreach (var annotation in Document.Annotations)
        {
            bool hideText = _editing != null && annotation.Id == _editing.Id;
            AnnotationPainter.Draw(dc, annotation, hideText);
            if (_selected == annotation.Id)
                DrawSelection(dc, annotation);
        }
        if (_draft != null)
            AnnotationPainter.Draw(dc, _draft, _editing != null && _draft.Id == _editing.Id);
        if (_editing != null && _editIsNew)
            AnnotationPainter.Draw(dc, _editing, hideText: true);
        AnnotationPainter.DrawSpotlight(dc, Document, _draft);
        dc.Pop();
        dc.Pop();
    }

    private static void DrawSelection(DrawingContext dc, Annotation annotation)
    {
        var rect = annotation.Kind == AnnotationKind.Callout
            ? AnnotationPainter.Bubble(annotation, out _)
            : AnnotationPainter.Box(annotation);
        var pen = new Pen(Brushes.White, 1) { DashStyle = DashStyles.Dash };
        dc.DrawRectangle(null, pen, rect);
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        var point = ToSource(e.GetPosition(this));
        if (Tool == EditorTool.Picker)
        {
            int x = (int)Math.Round(point.X);
            int y = (int)Math.Round(point.Y);
            if ((uint)x < (uint)Document.Source.Width && (uint)y < (uint)Document.Source.Height)
                ColorPicked?.Invoke(Document.Source.HexAt(x, y));
            return;
        }

        if (Tool == EditorTool.Select)
        {
            var hit = Hit(point);
            _selected = hit?.Id;
            if (hit != null && e.ClickCount >= 2 && hit.Kind is AnnotationKind.Text or AnnotationKind.Callout)
            {
                BeginEdit(hit, isNew: false);
                return;
            }
            if (hit != null)
            {
                _moving = hit;
                _press = point;
                _moveFrom = hit.Points.Select(p => p).ToList();
                Document.BeginEdit();
                CaptureMouse();
            }
            _paint.InvalidateVisual();
            return;
        }

        if (Tool == EditorTool.Text)
        {
            var mark = NewMark();
            mark.Kind = AnnotationKind.Text;
            mark.Points.Add(point);
            BeginEdit(mark, isNew: true);
            return;
        }

        if (Tool == EditorTool.Number)
        {
            var mark = NewMark();
            mark.Kind = AnnotationKind.Number;
            mark.Points.Add(point);
            Document.AddAnnotation(mark);
            _selected = mark.Id;
            return;
        }

        _cropping = Tool == EditorTool.Crop;
        _draft = NewMark();
        _draft.Points.Add(point);
        _draft.Points.Add(point);
        _press = point;
        CaptureMouse();
        _paint.InvalidateVisual();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;
        var point = ToSource(e.GetPosition(this));
        if (_moving != null && _moveFrom != null)
        {
            double dx = point.X - _press.X;
            double dy = point.Y - _press.Y;
            for (int i = 0; i < _moveFrom.Count && i < _moving.Points.Count; i++)
                _moving.Points[i] = new PointD(_moveFrom[i].X + dx, _moveFrom[i].Y + dy);
            _paint.InvalidateVisual();
            return;
        }
        if (_draft == null)
            return;
        var end = Constrain(_press, point);
        if (_draft.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
        {
            var last = _draft.Points[^1];
            if (Math.Abs(last.X - end.X) + Math.Abs(last.Y - end.Y) >= 1.4)
                _draft.Points.Add(end);
        }
        else
        {
            _draft.Points[^1] = end;
        }
        _paint.InvalidateVisual();
    }

    private void OnUp(object sender, MouseButtonEventArgs e) => FinishDrag(cancel: false);

    private void FinishDrag(bool cancel)
    {
        if (_finishing)
            return;
        _finishing = true;
        try
        {
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            if (_moving != null)
            {
                double dx = _moving.Points.Count > 0 && _moveFrom is { Count: > 0 }
                    ? _moving.Points[0].X - _moveFrom[0].X
                    : 0;
                double dy = _moving.Points.Count > 0 && _moveFrom is { Count: > 0 }
                    ? _moving.Points[0].Y - _moveFrom[0].Y
                    : 0;
                Document.EndEdit(!cancel && Math.Abs(dx) + Math.Abs(dy) > 1.5);
                _moving = null;
                _moveFrom = null;
                return;
            }
            var draft = _draft;
            bool cropping = _cropping;
            _draft = null;
            _cropping = false;
            if (draft == null)
                return;
            if (cancel || !IsBigEnough(draft))
            {
                _paint.InvalidateVisual();
                return;
            }
            if (cropping)
            {
                var (x, y, w, h) = ShapeMath.Bounds(draft.Points);
                Document.SetView(new IntRect((int)Math.Floor(x), (int)Math.Floor(y), (int)Math.Round(w), (int)Math.Round(h)));
                return;
            }
            if (draft.Kind == AnnotationKind.Callout)
            {
                BeginEdit(draft, isNew: true);
                return;
            }
            Document.AddAnnotation(draft);
            _selected = draft.Id;
        }
        finally
        {
            _finishing = false;
        }
    }

    private void BeginEdit(Annotation annotation, bool isNew)
    {
        CommitText();
        _editing = annotation;
        _editIsNew = isNew;
        _editOriginal = annotation.Text;
        EnsureBox();
        _box!.Text = isNew && annotation.Kind == AnnotationKind.Callout && string.IsNullOrEmpty(annotation.Text)
            ? "Note"
            : annotation.Text;
        bool lightInk = annotation.Kind == AnnotationKind.Callout && !ShapeMath.IsDark(annotation.ColorHex);
        _box.Foreground = lightInk ? Palette.Ink : Palette.Brush(annotation.ColorHex);
        _box.FontSize = Math.Max(10, annotation.FontSize / PixelScale);
        _box.FontWeight = FontWeights.Normal;
        _box.Visibility = Visibility.Visible;
        PositionTextBox();
        _paint.InvalidateVisual();
        Dispatcher.BeginInvoke(() =>
        {
            _box?.Focus();
            _box?.SelectAll();
        });
    }

    private void CommitText()
    {
        if (_suppress || _editing == null || _box == null || _box.Visibility != Visibility.Visible)
            return;
        var annotation = _editing;
        string text = _box.Text ?? "";
        bool isNew = _editIsNew;
        _editing = null;
        _box.Visibility = Visibility.Collapsed;
        if (isNew)
        {
            if (annotation.Kind == AnnotationKind.Text && string.IsNullOrWhiteSpace(text))
            {
                _paint.InvalidateVisual();
                return;
            }
            annotation.Text = string.IsNullOrWhiteSpace(text) ? "Note" : text;
            Document.AddAnnotation(annotation);
            _selected = annotation.Id;
            return;
        }
        Document.UpdateText(annotation.Id, text);
    }

    private void EnsureBox()
    {
        if (_box != null)
            return;
        _box = new TextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = Palette.Font,
            CaretBrush = Brushes.White,
            Visibility = Visibility.Collapsed
        };
        _box.LostKeyboardFocus += (_, _) => CommitText();
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                CancelText();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                CommitText();
                e.Handled = true;
            }
        };
        _textLayer.Children.Add(_box);
    }

    private void PositionTextBox()
    {
        if (_box == null || _editing == null || _box.Visibility != Visibility.Visible)
            return;
        if (_editing.Kind == AnnotationKind.Callout && _editing.Points.Count > 0)
        {
            var bubble = AnnotationPainter.Bubble(_editing, out _);
            Canvas.SetLeft(_box, (bubble.X - Document.View.X) / PixelScale + 8);
            Canvas.SetTop(_box, (bubble.Y - Document.View.Y) / PixelScale + 6);
            _box.Width = Math.Max(40, bubble.Width / PixelScale - 16);
            return;
        }
        if (_editing.Points.Count == 0)
            return;
        Canvas.SetLeft(_box, (_editing.Points[0].X - Document.View.X) / PixelScale);
        Canvas.SetTop(_box, (_editing.Points[0].Y - Document.View.Y) / PixelScale);
        _box.Width = Math.Max(80, 240 / PixelScale);
    }

    private Annotation NewMark() => new()
    {
        Kind = Tool switch
        {
            EditorTool.Pen => AnnotationKind.Pen,
            EditorTool.Line => AnnotationKind.Line,
            EditorTool.Arrow => AnnotationKind.Arrow,
            EditorTool.Rectangle => AnnotationKind.Rectangle,
            EditorTool.Ellipse => AnnotationKind.Ellipse,
            EditorTool.Highlighter => AnnotationKind.Highlighter,
            EditorTool.Callout => AnnotationKind.Callout,
            EditorTool.Blur => AnnotationKind.Blur,
            EditorTool.Spotlight => AnnotationKind.Spotlight,
            EditorTool.Crop => AnnotationKind.Rectangle,
            _ => AnnotationKind.Pen
        },
        ColorHex = ColorHex,
        Stroke = StrokeWidth,
        FontSize = FontSize
    };

    private PointD ToSource(Point dip) => new(dip.X * PixelScale + Document.View.X, dip.Y * PixelScale + Document.View.Y);

    private PointD Constrain(PointD start, PointD end)
    {
        if (Keyboard.Modifiers != ModifierKeys.Shift)
            return end;
        return _draft?.Kind is AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Pen or AnnotationKind.Highlighter
            ? ShapeMath.ConstrainLine(start, end)
            : ShapeMath.ConstrainSquare(start, end);
    }

    private static bool IsBigEnough(Annotation annotation)
    {
        if (annotation.Kind is AnnotationKind.Pen or AnnotationKind.Highlighter)
            return annotation.Points.Count >= 2;
        var (_, _, w, h) = ShapeMath.Bounds(annotation.Points);
        return w >= 3 || h >= 3;
    }

    private Annotation? Hit(PointD point)
    {
        for (int i = Document.Annotations.Count - 1; i >= 0; i--)
        {
            if (Hits(Document.Annotations[i], point))
                return Document.Annotations[i];
        }
        return null;
    }

    private static bool Hits(Annotation annotation, PointD point)
    {
        if (annotation.Kind is AnnotationKind.Pen or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Highlighter)
            return Distance(annotation.Points, point) <= Math.Max(8, (annotation.Kind == AnnotationKind.Highlighter ? annotation.Stroke * 4 : annotation.Stroke) + 4);
        var rect = annotation.Kind == AnnotationKind.Callout
            ? AnnotationPainter.Bubble(annotation, out _)
            : AnnotationPainter.Box(annotation);
        if (annotation.Kind == AnnotationKind.Text)
            rect = new Rect(rect.X, rect.Y, Math.Max(rect.Width, Math.Max(24, annotation.Text.Length * annotation.FontSize * 0.55)), Math.Max(rect.Height, annotation.FontSize * 1.4));
        rect.Inflate(6, 6);
        return rect.Contains(point.X, point.Y);
    }

    private static double Distance(IReadOnlyList<PointD> points, PointD point)
    {
        if (points.Count == 0)
            return double.MaxValue;
        if (points.Count == 1)
            return double.Hypot(points[0].X - point.X, points[0].Y - point.Y);
        double best = double.MaxValue;
        for (int i = 1; i < points.Count; i++)
            best = Math.Min(best, Segment(points[i - 1], points[i], point));
        return best;
    }

    private static double Segment(PointD a, PointD b, PointD p)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double len2 = dx * dx + dy * dy;
        double t = len2 < 0.001 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
        double x = a.X + t * dx;
        double y = a.Y + t * dy;
        return double.Hypot(p.X - x, p.Y - y);
    }

    private sealed class PaintLayer : FrameworkElement
    {
        public Action<DrawingContext>? Paint;
        protected override void OnRender(DrawingContext drawingContext) => Paint?.Invoke(drawingContext);
    }
}
