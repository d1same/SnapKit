namespace SnapKit;

public enum AnnotationHandleKind
{
    Start,
    End,
    NorthWest,
    NorthEast,
    SouthEast,
    SouthWest
}

public readonly record struct AnnotationHandle(AnnotationHandleKind Kind, PointD Position);

public static class AnnotationHandles
{
    public static IReadOnlyList<AnnotationHandle> For(Annotation annotation)
    {
        if (annotation.Points.Count < 2)
            return Array.Empty<AnnotationHandle>();

        switch (annotation.Kind)
        {
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
            case AnnotationKind.Callout:
                return new[]
                {
                    new AnnotationHandle(AnnotationHandleKind.Start, annotation.Points[0]),
                    new AnnotationHandle(AnnotationHandleKind.End, annotation.Points[1])
                };

            case AnnotationKind.Rectangle:
            case AnnotationKind.Ellipse:
            case AnnotationKind.Blur:
            case AnnotationKind.Redact:
            case AnnotationKind.Spotlight:
            case AnnotationKind.Magnifier:
                var (x, y, w, h) = ShapeMath.Bounds(annotation.Points);
                return new[]
                {
                    new AnnotationHandle(AnnotationHandleKind.NorthWest, new PointD(x, y)),
                    new AnnotationHandle(AnnotationHandleKind.NorthEast, new PointD(x + w, y)),
                    new AnnotationHandle(AnnotationHandleKind.SouthEast, new PointD(x + w, y + h)),
                    new AnnotationHandle(AnnotationHandleKind.SouthWest, new PointD(x, y + h))
                };

            default:
                return Array.Empty<AnnotationHandle>();
        }
    }

    public static PointD AnchorFor(Annotation annotation, AnnotationHandleKind handle)
    {
        if (annotation.Points.Count < 2)
            return annotation.Points.Count > 0 ? annotation.Points[0] : default;

        if (handle == AnnotationHandleKind.Start)
            return annotation.Points[1];
        if (handle == AnnotationHandleKind.End)
            return annotation.Points[0];

        var (x, y, w, h) = ShapeMath.Bounds(annotation.Points);
        return handle switch
        {
            AnnotationHandleKind.NorthWest => new PointD(x + w, y + h),
            AnnotationHandleKind.NorthEast => new PointD(x, y + h),
            AnnotationHandleKind.SouthEast => new PointD(x, y),
            AnnotationHandleKind.SouthWest => new PointD(x + w, y),
            _ => annotation.Points[0]
        };
    }

    public static void ApplyResize(Annotation annotation, AnnotationHandleKind handle, PointD anchor, PointD target, bool constrain)
    {
        if (annotation.Points.Count < 2)
            return;

        if (handle is AnnotationHandleKind.Start or AnnotationHandleKind.End)
        {
            var moved = constrain && annotation.Kind is AnnotationKind.Line or AnnotationKind.Arrow
                ? ShapeMath.ConstrainLine(anchor, target)
                : target;
            if (handle == AnnotationHandleKind.Start)
            {
                annotation.Points[0] = moved;
                annotation.Points[1] = anchor;
            }
            else
            {
                annotation.Points[0] = anchor;
                annotation.Points[1] = moved;
            }
            return;
        }

        var corner = constrain ? ShapeMath.ConstrainSquare(anchor, target) : target;
        double minX = Math.Min(anchor.X, corner.X);
        double minY = Math.Min(anchor.Y, corner.Y);
        double maxX = Math.Max(anchor.X, corner.X);
        double maxY = Math.Max(anchor.Y, corner.Y);
        if (maxX - minX < 2)
            maxX = minX + 2;
        if (maxY - minY < 2)
            maxY = minY + 2;

        annotation.Points[0] = new PointD(minX, minY);
        annotation.Points[1] = new PointD(maxX, maxY);
    }
}
