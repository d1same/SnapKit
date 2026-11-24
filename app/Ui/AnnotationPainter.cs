using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnapKit;

internal static class AnnotationPainter
{
    public static void Draw(DrawingContext dc, Annotation annotation, bool hideText = false, ImageSource? underlay = null, IntRect? view = null)
    {
        if (annotation.Points.Count == 0 || annotation.Kind is AnnotationKind.Blur or AnnotationKind.Spotlight)
            return;

        var fill = Palette.Brush(annotation.ColorHex);
        switch (annotation.Kind)
        {
            case AnnotationKind.Pen:
                DrawStroke(dc, annotation, annotation.Stroke, 255);
                break;
            case AnnotationKind.Highlighter:
                DrawStroke(dc, annotation, Math.Max(16, annotation.Stroke * 5), 96);
                break;
            case AnnotationKind.Line:
                if (annotation.Points.Count >= 2)
                    dc.DrawLine(Pen(annotation, 255), P(annotation.Points[0]), P(annotation.Points[1]));
                break;
            case AnnotationKind.Arrow:
                DrawArrow(dc, annotation);
                break;
            case AnnotationKind.Rectangle:
                DrawShapeBox(dc, annotation, ellipse: false);
                break;
            case AnnotationKind.Ellipse:
                DrawShapeBox(dc, annotation, ellipse: true);
                break;
            case AnnotationKind.Redact:
                dc.DrawRectangle(fill, null, Box(annotation));
                break;
            case AnnotationKind.Text:
                if (!hideText)
                    DrawPlainText(dc, annotation);
                break;
            case AnnotationKind.Callout:
                DrawCallout(dc, annotation, hideText);
                break;
            case AnnotationKind.Number:
                DrawNumber(dc, annotation, fill);
                break;
            case AnnotationKind.Magnifier:
                DrawMagnifier(dc, annotation, underlay, view);
                break;
        }
    }

    private static void DrawMagnifier(DrawingContext dc, Annotation annotation, ImageSource? underlay, IntRect? view)
    {
        var rect = Box(annotation);
        double diameter = Math.Min(rect.Width, rect.Height);
        if (diameter < 10)
            return;
        double radius = diameter * 0.5;
        var center = new Point(rect.X + rect.Width * 0.5, rect.Y + rect.Height * 0.5);

        // Soft drop shadow behind the circular loupe
        var shadowBrush = Palette.Freeze(0, 0, 0, 0x48);
        dc.DrawEllipse(shadowBrush, null, new Point(center.X, center.Y + 4), radius + 3, radius + 3);
        dc.DrawEllipse(Palette.Freeze(0, 0, 0, 0x26), null, new Point(center.X, center.Y + 6), radius + 6, radius + 6);

        // 2x zoomed interior clipped to the loupe circle
        var clip = new EllipseGeometry(center, radius, radius);
        clip.Freeze();
        dc.PushClip(clip);
        dc.DrawEllipse(Palette.Bg, null, center, radius, radius);

        if (underlay != null && view is { Width: > 0, Height: > 0 } v)
        {
            const double zoom = 2.0;
            double localCx = center.X - v.X;
            double localCy = center.Y - v.Y;
            double srcRadius = radius / zoom;
            double srcX = localCx - srcRadius;
            double srcY = localCy - srcRadius;
            double srcW = srcRadius * 2.0;
            double srcH = srcRadius * 2.0;

            var brush = new ImageBrush(underlay)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(srcX, srcY, srcW, srcH),
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            dc.DrawEllipse(brush, null, center, radius, radius);
        }
        dc.Pop();

        // Crisp inner white glass rim + outer colored stroke
        double ringWidth = Math.Clamp(annotation.Stroke, 2, 8);
        var innerPen = new Pen(Palette.Freeze(255, 255, 255, 0xDD), 1.5);
        innerPen.Freeze();
        var outerPen = new Pen(Palette.Brush(annotation.ColorHex), ringWidth);
        outerPen.Freeze();
        dc.DrawEllipse(null, innerPen, center, Math.Max(1, radius - ringWidth * 0.5 - 0.5), Math.Max(1, radius - ringWidth * 0.5 - 0.5));
        dc.DrawEllipse(null, outerPen, center, radius, radius);
    }

    private static void DrawShapeBox(DrawingContext dc, Annotation annotation, bool ellipse)
    {
        var rect = Box(annotation);
        Brush? fill = annotation.FillMode switch
        {
            ShapeFillMode.Tint => Palette.Brush(annotation.ColorHex, 0x3A),
            ShapeFillMode.Solid => Palette.Brush(annotation.ColorHex, 255),
            _ => null
        };
        Pen? pen = annotation.FillMode == ShapeFillMode.Solid ? null : Pen(annotation, 255);
        if (ellipse)
            dc.DrawEllipse(fill, pen, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
        else
            dc.DrawRectangle(fill, pen, rect);
    }

    public static void DrawSpotlight(DrawingContext dc, ShotDocument document, Annotation? draft)
    {
        Annotation? spot = draft is { Kind: AnnotationKind.Spotlight, Points.Count: >= 2 }
            ? draft
            : document.Annotations.LastOrDefault(a => a.Kind == AnnotationKind.Spotlight && a.Points.Count >= 2);
        if (spot == null)
            return;
        var hole = Box(spot);
        if (hole.Width < 2 || hole.Height < 2)
            return;
        var view = new Rect(document.View.X, document.View.Y, document.View.Width, document.View.Height);
        var dim = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(view), new RectangleGeometry(hole));
        dc.DrawGeometry(Palette.Brush(spot.ColorHex, 0x8C), null, dim);
    }

    public static Rect Bubble(Annotation annotation, out FormattedText text)
    {
        var anchor = annotation.Points.Count > 1 ? P(annotation.Points[1]) : new Point(P(annotation.Points[0]).X + 28, P(annotation.Points[0]).Y - 48);
        string value = string.IsNullOrWhiteSpace(annotation.Text) ? "Note" : annotation.Text;
        text = Formatted(value, annotation.FontSize, Brushes.White, 280, bold: false);
        double width = Math.Max(28, text.Width + 20);
        double height = text.Height + 14;
        return new Rect(anchor.X - width / 2, anchor.Y - height / 2, width, height);
    }

    private static void DrawPlainText(DrawingContext dc, Annotation annotation)
    {
        if (string.IsNullOrEmpty(annotation.Text))
            return;
        var origin = P(annotation.Points[0]);
        var halo = Formatted(annotation.Text, annotation.FontSize, Contrast(annotation.ColorHex), 520, bold: false);
        foreach (var (dx, dy) in new[] { (-1d, 0d), (1d, 0d), (0d, -1d), (0d, 1d) })
            dc.DrawText(halo, new Point(origin.X + dx, origin.Y + dy));
        dc.DrawText(Formatted(annotation.Text, annotation.FontSize, Palette.Brush(annotation.ColorHex), 520, bold: false), origin);
    }

    private static void DrawCallout(DrawingContext dc, Annotation annotation, bool hideText)
    {
        var bubble = Bubble(annotation, out var text);
        var fill = Palette.Brush(annotation.ColorHex);
        text.SetForegroundBrush(Contrast(annotation.ColorHex));
        var tip = P(annotation.Points[0]);
        var edge = Edge(bubble, tip);
        double dx = tip.X - edge.X;
        double dy = tip.Y - edge.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 1)
        {
            double nx = -dy / len;
            double ny = dx / len;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(new Point(edge.X + nx * 7, edge.Y + ny * 7), true, false);
                ctx.LineTo(new Point(edge.X - nx * 7, edge.Y - ny * 7), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(fill, null, geo);
        }
        var outline = new Pen(Contrast(annotation.ColorHex), 1);
        outline.Freeze();
        dc.DrawRoundedRectangle(fill, outline, bubble, 7, 7);
        if (!hideText)
            dc.DrawText(text, new Point(bubble.X + 10, bubble.Y + 6));
    }

    private static void DrawNumber(DrawingContext dc, Annotation annotation, SolidColorBrush fill)
    {
        var center = P(annotation.Points[0]);
        double radius = Math.Max(12, annotation.FontSize * 0.72);
        var ring = new Pen(Contrast(annotation.ColorHex), 1.25);
        ring.Freeze();
        dc.DrawEllipse(fill, ring, center, radius, radius);
        var text = Formatted(annotation.Number.ToString(), annotation.FontSize * 0.82, Contrast(annotation.ColorHex), 40, bold: true);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2 - 0.5));
    }

    private static void DrawArrow(DrawingContext dc, Annotation annotation)
    {
        if (annotation.Points.Count < 2)
            return;
        var start = annotation.Points[0];
        var end = annotation.Points[1];
        double head = Math.Max(11, annotation.Stroke * 3.6);
        var (left, right) = ShapeMath.ArrowHead(start, end, head);
        var neck = new PointD((left.X + right.X) / 2, (left.Y + right.Y) / 2);
        var into = new PointD(neck.X + (end.X - neck.X) * 0.45, neck.Y + (end.Y - neck.Y) * 0.45);
        dc.DrawLine(Pen(annotation, 255), P(start), P(into));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(end), true, true);
            ctx.LineTo(P(left), true, false);
            ctx.LineTo(P(right), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(Palette.Brush(annotation.ColorHex), null, geo);
    }

    private static void DrawStroke(DrawingContext dc, Annotation annotation, double thickness, byte alpha)
    {
        if (annotation.Points.Count == 1)
        {
            dc.DrawEllipse(Palette.Brush(annotation.ColorHex, alpha), null, P(annotation.Points[0]), thickness / 2, thickness / 2);
            return;
        }
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(annotation.Points[0]), false, false);
            for (int i = 1; i < annotation.Points.Count; i++)
                ctx.LineTo(P(annotation.Points[i]), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, Pen(annotation, alpha, thickness), geo);
    }

    private static Pen Pen(Annotation annotation, byte alpha, double? thickness = null)
    {
        var pen = new Pen(Palette.Brush(annotation.ColorHex, alpha), thickness ?? Math.Max(1, annotation.Stroke))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    public static Rect Box(Annotation annotation)
    {
        var (x, y, w, h) = ShapeMath.Bounds(annotation.Points);
        return new Rect(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    private static FormattedText Formatted(string value, double size, Brush brush, double maxWidth, bool bold)
    {
        var text = new FormattedText(
            value,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(Palette.Font, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            Math.Max(8, size),
            brush,
            1.0)
        {
            MaxTextWidth = maxWidth
        };
        return text;
    }

    private static Point Edge(Rect rect, Point tip)
    {
        var center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        double dx = tip.X - center.X;
        double dy = tip.Y - center.Y;
        if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01)
            return new Point(rect.X, center.Y);
        double scaleX = dx == 0 ? double.PositiveInfinity : (rect.Width / 2) / Math.Abs(dx);
        double scaleY = dy == 0 ? double.PositiveInfinity : (rect.Height / 2) / Math.Abs(dy);
        double scale = Math.Min(scaleX, scaleY);
        return new Point(center.X + dx * scale, center.Y + dy * scale);
    }

    private static Brush Contrast(string hex) => ShapeMath.IsDark(hex) ? Brushes.White : Palette.Ink;

    private static Point P(PointD point) => new(point.X, point.Y);
}
