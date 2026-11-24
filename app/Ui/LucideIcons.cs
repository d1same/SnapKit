using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SnapKit;

internal enum LucideIcon
{
    Select,
    Arrow,
    Pen,
    Rectangle,
    Ellipse,
    Highlighter,
    Text,
    Callout,
    Number,
    Blur,
    Redact,
    Spotlight,
    Crop,
    Eyedropper,
    Undo,
    Redo,
    Trash,
    Ocr,
    Pin,
    Save,
    FolderDown,
    Copy,
    Close,
    Play,
    Pause,
    Stop,
    Video,
    Film,
    Audio,
    Mic,
    ExternalLink,
    ZoomIn,
    ZoomOut,
    Minus,
    Plus,
    Sparkles,
    Check,
    Table,
    AlignLeft
}

/// <summary>
/// Crisp 24x24 vector icons adapted from the free MIT/ISC Lucide icon pack (https://lucide.dev).
/// Rendered inside a fixed 24x24 Canvas wrapped by a Viewbox so every icon preserves exact
/// optical proportions, centering, and uniform rounded stroke weight at any DPI.
/// </summary>
internal static class LucideIcons
{
    public static Viewbox Create(LucideIcon icon, Brush? stroke = null, double size = 15, double strokeWidth = 1.9, bool filled = false)
    {
        var ink = stroke ?? Palette.Text;
        var canvas = new Canvas
        {
            Width = 24,
            Height = 24,
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        void Path(string data, bool fill = false)
        {
            var p = new Path
            {
                Data = Geometry.Parse(data),
                Stroke = ink,
                StrokeThickness = strokeWidth,
                Fill = (fill || filled) ? ink : Brushes.Transparent,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            canvas.Children.Add(p);
        }

        void Rect(double x, double y, double w, double h, double rx = 0, bool fill = false)
        {
            var r = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = rx,
                RadiusY = rx,
                Stroke = ink,
                StrokeThickness = strokeWidth,
                Fill = (fill || filled) ? ink : Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            canvas.Children.Add(r);
        }

        void Circle(double cx, double cy, double r, bool fill = false)
        {
            var e = new Ellipse
            {
                Width = r * 2,
                Height = r * 2,
                Stroke = ink,
                StrokeThickness = strokeWidth,
                Fill = (fill || filled) ? ink : Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(e, cx - r);
            Canvas.SetTop(e, cy - r);
            canvas.Children.Add(e);
        }

        void Line(double x1, double y1, double x2, double y2)
        {
            var l = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = ink,
                StrokeThickness = strokeWidth,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false
            };
            canvas.Children.Add(l);
        }

        switch (icon)
        {
            // Lucide: mouse-pointer-2
            case LucideIcon.Select:
                Path("M 4.04 4.69 A 0.5 0.5 0 0 1 4.69 4.04 L 20.69 10.54 A 0.5 0.5 0 0 1 20.63 11.48 L 14.5 13.06 A 2 2 0 0 0 13.06 14.5 L 11.48 20.63 A 0.5 0.5 0 0 1 10.54 20.69 Z");
                break;

            // Lucide: arrow-up-right
            case LucideIcon.Arrow:
                Path("M 7 7 H 17 V 17");
                Line(7, 17, 17, 7);
                break;

            // Lucide: pencil
            case LucideIcon.Pen:
                Path("M 21.17 6.81 A 1 1 0 0 0 17.19 2.83 L 3.84 16.17 A 2 2 0 0 0 3.34 17 L 2.02 21.36 A 0.5 0.5 0 0 0 2.64 21.98 L 7 20.66 A 2 2 0 0 0 7.83 20.16 Z");
                Line(15, 5, 19, 9);
                break;

            // Lucide: square
            case LucideIcon.Rectangle:
                Rect(3, 3, 18, 18, rx: 2.5);
                break;

            // Lucide: circle
            case LucideIcon.Ellipse:
                Circle(12, 12, 9.2);
                break;

            // Lucide: highlighter
            case LucideIcon.Highlighter:
                Path("M 9 11 L 3 17 V 20 H 12 L 15 17");
                Path("M 22 12 L 17.4 16.6 A 2 2 0 0 1 14.6 16.6 L 9.4 11.4 A 2 2 0 0 1 9.4 8.6 L 14 4");
                break;

            // Lucide: type
            case LucideIcon.Text:
                Line(12, 4, 12, 20);
                Path("M 4 7 V 5 A 1 1 0 0 1 5 4 H 19 A 1 1 0 0 1 20 5 V 7");
                Line(9, 20, 15, 20);
                break;

            // Lucide: message-square
            case LucideIcon.Callout:
                Path("M 21 15 A 2 2 0 0 1 19 17 H 7 L 3 21 V 5 A 2 2 0 0 1 5 3 H 19 A 2 2 0 0 1 21 5 Z");
                break;

            // Lucide: circle + step "1"
            case LucideIcon.Number:
                Circle(12, 12, 9.2);
                Path("M 10 9.5 L 12 8 V 16");
                Line(10, 16, 14, 16);
                break;

            // Lucide: grid-2x2 (pixelate mosaic)
            case LucideIcon.Blur:
                Rect(3, 3, 18, 18, rx: 2);
                Line(12, 3, 12, 21);
                Line(3, 12, 21, 12);
                break;

            // Lucide: rectangle-horizontal (filled blackout redact)
            case LucideIcon.Redact:
                Rect(2.5, 6, 19, 12, rx: 2.5, fill: true);
                break;

            // Lucide: focus (spotlight target)
            case LucideIcon.Spotlight:
                Circle(12, 12, 3.2);
                Path("M 3 7 V 5 A 2 2 0 0 1 5 3 H 7");
                Path("M 17 3 H 19 A 2 2 0 0 1 21 5 V 7");
                Path("M 21 17 V 19 A 2 2 0 0 1 19 21 H 17");
                Path("M 7 21 H 5 A 2 2 0 0 1 3 19 V 17");
                break;

            // Lucide: crop
            case LucideIcon.Crop:
                Path("M 6 2 V 16 A 2 2 0 0 0 8 18 H 22");
                Path("M 18 22 V 8 A 2 2 0 0 0 16 6 H 2");
                break;

            // Lucide: pipette
            case LucideIcon.Eyedropper:
                Path("M 12 9 L 3.59 17.41 A 2 2 0 0 0 3 18.83 V 20.17 A 2 2 0 0 1 2.41 21.59 A 2 2 0 0 1 3.83 21 H 5.17 A 2 2 0 0 0 6.59 20.41 L 15 12");
                Path("M 18 9 L 18.4 9.4 A 1 1 0 1 1 15.4 12.4 L 11.6 8.6 A 1 1 0 1 1 14.6 5.6 L 15 6 L 18.4 2.6 A 1 1 0 1 1 21.4 5.6 Z");
                Line(2, 22, 2.45, 21.55);
                break;

            // Lucide: undo-2
            case LucideIcon.Undo:
                Path("M 9 14 L 4 9 L 9 4");
                Path("M 4 9 H 14.5 A 5.5 5.5 0 0 1 20 14.5 A 5.5 5.5 0 0 1 14.5 20 H 11");
                break;

            // Lucide: redo-2
            case LucideIcon.Redo:
                Path("M 15 14 L 20 9 L 15 4");
                Path("M 20 9 H 9.5 A 5.5 5.5 0 0 0 4 14.5 A 5.5 5.5 0 0 0 9.5 20 H 13");
                break;

            // Lucide: trash
            case LucideIcon.Trash:
                Line(3, 6, 21, 6);
                Path("M 19 6 V 20 A 2 2 0 0 1 17 22 H 7 A 2 2 0 0 1 5 20 V 6");
                Path("M 8 6 V 4 A 2 2 0 0 1 10 2 H 14 A 2 2 0 0 1 16 4 V 6");
                Line(10, 11, 10, 17);
                Line(14, 11, 14, 17);
                break;

            // Lucide: scan-text
            case LucideIcon.Ocr:
                Path("M 3 7 V 5 A 2 2 0 0 1 5 3 H 7");
                Path("M 17 3 H 19 A 2 2 0 0 1 21 5 V 7");
                Path("M 21 17 V 19 A 2 2 0 0 1 19 21 H 17");
                Path("M 7 21 H 5 A 2 2 0 0 1 3 19 V 17");
                Line(7, 8, 15, 8);
                Line(7, 12, 17, 12);
                Line(7, 16, 13, 16);
                break;

            // Lucide: pin
            case LucideIcon.Pin:
                Line(12, 17, 12, 22);
                Path("M 9 10.76 A 2 2 0 0 1 7.89 12.55 L 6.11 13.45 A 2 2 0 0 0 5 15.24 V 16 A 1 1 0 0 0 6 17 H 18 A 1 1 0 0 0 19 16 V 15.24 A 2 2 0 0 0 17.89 13.45 L 16.11 12.55 A 2 2 0 0 1 15 10.76 V 7 A 1 1 0 0 1 16 6 A 2 2 0 0 0 16 2 H 8 A 2 2 0 0 0 8 6 A 1 1 0 0 1 9 7 Z");
                break;

            // Lucide: save
            case LucideIcon.Save:
                Path("M 15.2 3 A 2 2 0 0 1 16.6 3.6 L 20.4 7.4 A 2 2 0 0 1 21 8.8 V 19 A 2 2 0 0 1 19 21 H 5 A 2 2 0 0 1 3 19 V 5 A 2 2 0 0 1 5 3 Z");
                Path("M 17 21 V 14 A 1 1 0 0 0 16 13 H 8 A 1 1 0 0 0 7 14 V 21");
                Path("M 7 3 V 7 A 1 1 0 0 0 8 8 H 15");
                break;

            // Lucide: folder-down
            case LucideIcon.FolderDown:
                Path("M 20 20 A 2 2 0 0 0 22 18 V 8 A 2 2 0 0 0 20 6 H 12.1 A 2 2 0 0 1 10.41 5.1 L 9.6 3.9 A 2 2 0 0 0 7.93 3 H 4 A 2 2 0 0 0 2 5 V 18 A 2 2 0 0 0 4 20 Z");
                Line(12, 10, 12, 16);
                Path("M 15 13 L 12 16 L 9 13");
                break;

            // Lucide: copy
            case LucideIcon.Copy:
                Rect(8, 8, 13.5, 13.5, rx: 2);
                Path("M 4 16 C 2.9 16 2 15.1 2 14 V 4 C 2 2.9 2.9 2 4 2 H 14 C 15.1 2 16 2.9 16 4");
                break;

            // Lucide: x
            case LucideIcon.Close:
                Line(18, 6, 6, 18);
                Line(6, 6, 18, 18);
                break;

            // Lucide: play
            case LucideIcon.Play:
                Path("M 6 4.75 A 1.5 1.5 0 0 1 8.26 3.45 L 19.26 10.7 A 1.5 1.5 0 0 1 19.26 13.3 L 8.26 20.55 A 1.5 1.5 0 0 1 6 19.25 Z", fill: filled);
                break;

            // Lucide: pause
            case LucideIcon.Pause:
                Rect(5, 4, 4.5, 16, rx: 1, fill: filled);
                Rect(14.5, 4, 4.5, 16, rx: 1, fill: filled);
                break;

            // Lucide: stop square
            case LucideIcon.Stop:
                Rect(5.5, 5.5, 13, 13, rx: 2, fill: true);
                break;

            // Lucide: video
            case LucideIcon.Video:
                Path("M 16 13 L 21.22 16.48 A 0.5 0.5 0 0 0 22 16.07 V 7.87 A 0.5 0.5 0 0 0 21.25 7.44 L 16 10.5");
                Rect(2, 6, 14, 12, rx: 2);
                break;

            // Lucide: film
            case LucideIcon.Film:
                Rect(3, 3, 18, 18, rx: 2);
                Line(7, 3, 7, 21);
                Line(17, 3, 17, 21);
                Line(3, 12, 21, 12);
                Line(3, 7.5, 7, 7.5);
                Line(3, 16.5, 7, 16.5);
                Line(17, 7.5, 21, 7.5);
                Line(17, 16.5, 21, 16.5);
                break;

            // Lucide: volume-2
            case LucideIcon.Audio:
                Path("M 11 4.7 A 0.7 0.7 0 0 0 9.8 4.2 L 6.41 7.59 A 1.4 1.4 0 0 1 5.42 8 H 3 A 1 1 0 0 0 2 9 V 15 A 1 1 0 0 0 3 16 H 5.42 A 1.4 1.4 0 0 1 6.41 16.41 L 9.8 19.8 A 0.7 0.7 0 0 0 11 19.3 Z");
                Path("M 15.8 9 A 4.8 4.8 0 0 1 15.8 15");
                Path("M 19.1 5.8 A 9 9 0 0 1 19.1 18.2");
                break;

            // Lucide: mic
            case LucideIcon.Mic:
                Line(12, 19, 12, 22);
                Path("M 19 10 V 12 A 7 7 0 0 1 5 12 V 10");
                Rect(9, 2, 6, 13, rx: 3);
                break;

            // Lucide: external-link
            case LucideIcon.ExternalLink:
                Path("M 15 3 H 21 V 9");
                Line(10, 14, 21, 3);
                Path("M 18 13 V 19 A 2 2 0 0 1 16 21 H 5 A 2 2 0 0 1 3 19 V 8 A 2 2 0 0 1 5 6 H 11");
                break;

            // Lucide: zoom-in
            case LucideIcon.ZoomIn:
                Circle(11, 11, 7.8);
                Line(21, 21, 16.65, 16.65);
                Line(11, 8, 11, 14);
                Line(8, 11, 14, 11);
                break;

            // Lucide: zoom-out
            case LucideIcon.ZoomOut:
                Circle(11, 11, 7.8);
                Line(21, 21, 16.65, 16.65);
                Line(8, 11, 14, 11);
                break;

            // Lucide: minus
            case LucideIcon.Minus:
                Line(5, 12, 19, 12);
                break;

            // Lucide: plus
            case LucideIcon.Plus:
                Line(5, 12, 19, 12);
                Line(12, 5, 12, 19);
                break;

            // Lucide: sparkles
            case LucideIcon.Sparkles:
                Path("M 11.02 2.81 A 1 1 0 0 1 12.98 2.81 L 14.03 8.37 A 2 2 0 0 0 15.63 9.97 L 21.19 11.02 A 1 1 0 0 1 21.19 12.98 L 15.63 14.03 A 2 2 0 0 0 14.03 15.63 L 12.98 21.19 A 1 1 0 0 1 11.02 21.19 L 9.97 15.63 A 2 2 0 0 0 8.37 14.03 L 2.81 12.98 A 1 1 0 0 1 2.81 11.02 L 8.37 9.97 A 2 2 0 0 0 9.97 8.37 Z");
                Line(20, 2, 20, 6);
                Line(18, 4, 22, 4);
                break;

            // Lucide: check
            case LucideIcon.Check:
                Path("M 20 6 L 9 17 L 4 12");
                break;

            // Lucide: table
            case LucideIcon.Table:
                Rect(3, 3, 18, 18, rx: 2);
                Line(3, 9, 21, 9);
                Line(3, 15, 21, 15);
                Line(9, 3, 9, 21);
                break;

            // Lucide: align-left
            case LucideIcon.AlignLeft:
                Line(4, 6, 20, 6);
                Line(4, 12, 14, 12);
                Line(4, 18, 18, 18);
                break;
        }

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Child = canvas
        };
    }

    public static void Recolor(DependencyObject node, Brush ink)
    {
        if (node is Shape shape)
        {
            shape.Stroke = ink;
            if (shape.Fill != null && shape.Fill != Brushes.Transparent)
                shape.Fill = ink;
            return;
        }
        if (node is Viewbox vb && vb.Child is DependencyObject vbChild)
        {
            Recolor(vbChild, ink);
            return;
        }
        if (node is Decorator dec && dec.Child is DependencyObject decChild)
        {
            Recolor(decChild, ink);
            return;
        }
        if (node is Panel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is DependencyObject dep)
                    Recolor(dep, ink);
            }
            return;
        }
        if (node is ContentControl cc && cc.Content is DependencyObject contentDep)
        {
            Recolor(contentDep, ink);
            return;
        }
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is DependencyObject child)
                Recolor(child, ink);
        }
    }
}
