using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SnapKit;

internal static class AnnotationPainter
{
    public static void Draw(DrawingContext dc, Annotation annotation, bool hideText = false)
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
                dc.DrawRectangle(null, Pen(annotation, 255), Box(annotation));
                break;
            case AnnotationKind.Ellipse:
                var rect = Box(annotation);
                dc.DrawEllipse(null, Pen(annotation, 255), new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), rect.Width / 2, rect.Height / 2);
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
        }
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
        dc.DrawGeometry(Palette.Freeze(0, 0, 0, 0x8C), null, dim);
    }

    public static Rect Bubble(Annotation annotation, out FormattedText text)
    {
        var anchor = annotation.Points.Count > 1 ? P(annotation.Points[1]) : new Point(P(annotation.Points[0]).X + 28, P(annotation.Points[0]).Y - 48);
        string value = string.IsNullOrWhiteSpace(annotation.Text) ? "Note" : annotation.Text;
        text = Formatted(value, annotation.FontSize, Brushes.White, 240, bold: false);
        double width = Math.Max(92, text.Width + 22);
        double height = text.Height + 18;
        return new Rect(anchor.X - width / 2, anchor.Y - height / 2, width, height);
    }

    private static void DrawPlainText(DrawingContext dc, Annotation annotation)
    {
        if (string.IsNullOrEmpty(annotation.Text))
            return;
        var text = Formatted(annotation.Text, annotation.FontSize, Palette.Brush(annotation.ColorHex), 520, bold: false);
        dc.DrawText(text, P(annotation.Points[0]));
    }

    private static void DrawCallout(DrawingContext dc, Annotation annotation, bool hideText)
    {
        var bubble = Bubble(annotation, out var text);
        var fill = Palette.Brush(annotation.ColorHex);
        var ink = ShapeMath.IsDark(annotation.ColorHex) ? Brushes.White : Palette.Ink;
        text.SetForegroundBrush(ink);
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
                ctx.LineTo(new Point(edge.X + nx * 8, edge.Y + ny * 8), true, false);
                ctx.LineTo(new Point(edge.X - nx * 8, edge.Y - ny * 8), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(fill, null, geo);
        }
        dc.DrawRoundedRectangle(fill, null, bubble, 8, 8);
        if (!hideText)
            dc.DrawText(text, new Point(bubble.X + 11, bubble.Y + 8));
    }

    private static void DrawNumber(DrawingContext dc, Annotation annotation, SolidColorBrush fill)
    {
        var center = P(annotation.Points[0]);
        double radius = Math.Max(12, annotation.FontSize * 0.72);
        dc.DrawEllipse(fill, null, center, radius, radius);
        var ink = ShapeMath.IsDark(annotation.ColorHex) ? Brushes.White : Palette.Ink;
        var text = Formatted(annotation.Number.ToString(), annotation.FontSize * 0.82, ink, 40, bold: true);
        dc.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2 - 0.5));
    }

    private static void DrawArrow(DrawingContext dc, Annotation annotation)
    {
        if (annotation.Points.Count < 2)
            return;
        var start = annotation.Points[0];
        var end = annotation.Points[1];
        double head = Math.Max(12, annotation.Stroke * 4);
        var (left, right) = ShapeMath.ArrowHead(start, end, head);
        var neck = new PointD((left.X + right.X) / 2, (left.Y + right.Y) / 2);
        dc.DrawLine(Pen(annotation, 255), P(start), P(neck));
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

    private static Point P(PointD point) => new(point.X, point.Y);
}
