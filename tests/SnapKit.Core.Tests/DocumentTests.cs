using SnapKit;

namespace SnapKit.Tests;

public class DocumentTests
{
    [Fact]
    public void Undo_restores_marks_and_crop()
    {
        var doc = new ShotDocument(new PixelBuffer(100, 80));
        doc.AddAnnotation(new Annotation
        {
            Kind = AnnotationKind.Arrow,
            Points = { new PointD(1, 1), new PointD(40, 20) }
        });
        Assert.Single(doc.Annotations);

        doc.Undo();
        Assert.Empty(doc.Annotations);
        doc.Redo();
        Assert.Single(doc.Annotations);

        doc.SetView(new IntRect(10, 5, 40, 30));
        Assert.Equal(40, doc.View.Width);
        doc.Undo();
        Assert.Equal(100, doc.View.Width);
        Assert.Equal(0, doc.View.X);
    }

    [Fact]
    public void Numbers_increment_and_undo()
    {
        var doc = new ShotDocument(new PixelBuffer(40, 40));
        doc.AddAnnotation(new Annotation { Kind = AnnotationKind.Number, Points = { new PointD(4, 4) } });
        doc.AddAnnotation(new Annotation { Kind = AnnotationKind.Number, Points = { new PointD(8, 8) } });
        Assert.Equal(1, doc.Annotations[0].Number);
        Assert.Equal(2, doc.Annotations[1].Number);
        doc.Undo();
        Assert.Single(doc.Annotations);
        doc.AddAnnotation(new Annotation { Kind = AnnotationKind.Number, Points = { new PointD(2, 2) } });
        Assert.Equal(2, doc.Annotations[1].Number);
    }

    [Fact]
    public void Move_cancel_restores_points()
    {
        var doc = new ShotDocument(new PixelBuffer(30, 30));
        var mark = new Annotation
        {
            Kind = AnnotationKind.Rectangle,
            Points = { new PointD(2, 2), new PointD(10, 8) }
        };
        doc.AddAnnotation(mark);
        doc.BeginEdit();
        mark.Points[1] = new PointD(20, 20);
        doc.EndEdit(changed: false);
        Assert.Equal(10, doc.Annotations[0].Points[1].X);
    }

    [Fact]
    public void Blur_changes_pixels_inside_the_rect_only()
    {
        var source = new PixelBuffer(12, 12);
        source.Fill(0, 0, 255);
        source.FillRect(0, 0, 2, 4, 0, 0, 0);
        source.FillRect(2, 0, 2, 4, 255, 255, 255);
        var doc = new ShotDocument(source);
        doc.AddAnnotation(new Annotation
        {
            Kind = AnnotationKind.Blur,
            Stroke = 4,
            Points = { new PointD(0, 0), new PointD(4, 4) }
        });
        var under = doc.ComposeUnderlay();
        Assert.Equal(255, under.GetPixel(8, 8).R);
        Assert.InRange(under.GetPixel(1, 1).R, 100, 160);
    }

    [Fact]
    public void Arrow_head_spreads()
    {
        var (left, right) = ShapeMath.ArrowHead(new PointD(0, 0), new PointD(40, 0), 12);
        Assert.NotEqual(left, right);
        Assert.NotEqual(left.Y, right.Y);
        Assert.True(Math.Abs(left.Y) > 1);
    }

    [Fact]
    public void Shift_constrain_snaps_to_square()
    {
        var end = ShapeMath.ConstrainSquare(new PointD(0, 0), new PointD(10, 3));
        Assert.Equal(10, end.X);
        Assert.Equal(10, end.Y);
    }
}
