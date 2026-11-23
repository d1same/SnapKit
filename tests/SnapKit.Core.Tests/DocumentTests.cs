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
    public void Restyle_changes_color_stroke_and_font_for_the_mark()
    {
        var doc = new ShotDocument(new PixelBuffer(40, 40));
        doc.AddAnnotation(new Annotation
        {
            Kind = AnnotationKind.Arrow,
            ColorHex = "#FF3B30",
            Stroke = 3,
            Points = { new PointD(1, 1), new PointD(10, 8) }
        });
        doc.AddAnnotation(new Annotation
        {
            Kind = AnnotationKind.Callout,
            ColorHex = "#1C1C1E",
            FontSize = 18,
            Text = "Note",
            Points = { new PointD(2, 2), new PointD(12, 12) }
        });
        doc.AddAnnotation(new Annotation
        {
            Kind = AnnotationKind.Blur,
            ColorHex = "#FF3B30",
            Stroke = 4,
            Points = { new PointD(0, 0), new PointD(8, 8) }
        });

        Assert.True(doc.Restyle(doc.Annotations[0].Id, "#0A84FF", 8, 32));
        Assert.Equal("#0A84FF", doc.Annotations[0].ColorHex);
        Assert.Equal(8, doc.Annotations[0].Stroke);

        Assert.True(doc.Restyle(doc.Annotations[1].Id, "#FFE14A", 12, 24));
        Assert.Equal("#FFE14A", doc.Annotations[1].ColorHex);
        Assert.Equal(3, doc.Annotations[1].Stroke);
        Assert.Equal(24, doc.Annotations[1].FontSize);

        Assert.True(doc.Restyle(doc.Annotations[2].Id, "#30D158", 6, 40));
        Assert.Equal("#FF3B30", doc.Annotations[2].ColorHex);
        Assert.Equal(6, doc.Annotations[2].Stroke);

        doc.Undo();
        Assert.Equal(4, doc.Annotations[2].Stroke);
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

    [Fact]
    public void Clear_all_and_reset_step_support_undo()
    {
        var doc = new ShotDocument(new PixelBuffer(60, 60));
        doc.AddAnnotation(new Annotation { Kind = AnnotationKind.Number, Points = { new PointD(5, 5) } });
        doc.AddAnnotation(new Annotation { Kind = AnnotationKind.Number, Points = { new PointD(15, 15) } });
        Assert.Equal(3, doc.NextNumber);

        Assert.True(doc.ResetNumbering());
        Assert.Equal(1, doc.NextNumber);
        Assert.False(doc.ResetNumbering());

        doc.Undo();
        Assert.Equal(3, doc.NextNumber);

        Assert.True(doc.ClearAnnotations());
        Assert.Empty(doc.Annotations);
        Assert.Equal(1, doc.NextNumber);

        doc.Undo();
        Assert.Equal(2, doc.Annotations.Count);
        Assert.Equal(3, doc.NextNumber);
    }

    [Fact]
    public void Duplicate_and_nudge_move_points_and_advance_step_numbers()
    {
        var doc = new ShotDocument(new PixelBuffer(80, 80));
        var rect = new Annotation
        {
            Kind = AnnotationKind.Rectangle,
            FillMode = ShapeFillMode.Tint,
            Points = { new PointD(10, 10), new PointD(30, 25) }
        };
        doc.AddAnnotation(rect);

        var copy = doc.DuplicateAnnotation(rect.Id, 16, 12);
        Assert.NotNull(copy);
        Assert.NotEqual(rect.Id, copy!.Id);
        Assert.Equal(ShapeFillMode.Tint, copy.FillMode);
        Assert.Equal(new PointD(26, 22), copy.Points[0]);
        Assert.Equal(new PointD(46, 37), copy.Points[1]);

        Assert.True(doc.NudgeAnnotation(copy.Id, -6, 3));
        Assert.Equal(new PointD(20, 25), copy.Points[0]);
        doc.Undo();
        Assert.Equal(new PointD(26, 22), doc.Annotations[1].Points[0]);

        var step = new Annotation { Kind = AnnotationKind.Number, Points = { new PointD(10, 10) } };
        doc.AddAnnotation(step);
        var stepCopy = doc.DuplicateAnnotation(step.Id, 10, 0);
        Assert.NotNull(stepCopy);
        Assert.Equal(1, step.Number);
        Assert.Equal(2, stepCopy!.Number);
    }

    [Fact]
    public void Redact_and_shape_fill_modes_restyle_properly()
    {
        var doc = new ShotDocument(new PixelBuffer(60, 60));
        var box = new Annotation
        {
            Kind = AnnotationKind.Rectangle,
            ColorHex = "#FF3B30",
            FillMode = ShapeFillMode.Outline,
            Points = { new PointD(4, 4), new PointD(24, 24) }
        };
        var redact = new Annotation
        {
            Kind = AnnotationKind.Redact,
            ColorHex = "#1C1C1E",
            Points = { new PointD(10, 10), new PointD(40, 20) }
        };
        doc.AddAnnotation(box);
        doc.AddAnnotation(redact);

        Assert.True(doc.Restyle(box.Id, null, null, null, ShapeFillMode.Tint));
        Assert.Equal(ShapeFillMode.Tint, box.FillMode);
        Assert.True(doc.Restyle(box.Id, null, null, null, ShapeFillMode.Solid));
        Assert.Equal(ShapeFillMode.Solid, box.FillMode);
        doc.Undo();
        Assert.Equal(ShapeFillMode.Tint, doc.Annotations[0].FillMode);

        Assert.True(doc.Restyle(redact.Id, "#0A84FF", 12, 32, ShapeFillMode.Solid));
        Assert.Equal("#0A84FF", doc.Annotations[1].ColorHex);
        Assert.Equal(3, doc.Annotations[1].Stroke);
        Assert.Equal(ShapeFillMode.Outline, doc.Annotations[1].FillMode);
    }

    [Fact]
    public void Backdrop_layout_and_document_undo_work()
    {
        var none = BackdropLayout.Measure(BackdropPreset.None, 400, 300);
        Assert.Equal(400, none.TotalWidth);
        Assert.Equal(300, none.TotalHeight);

        var clean = BackdropLayout.Measure(BackdropPreset.Clean, 400, 300);
        Assert.True(clean.TotalWidth > 400);
        Assert.True(clean.TotalHeight > 300);
        Assert.True(clean.PadX >= 24);
        Assert.True(clean.CornerRadius >= 8);

        var doc = new ShotDocument(new PixelBuffer(100, 80));
        Assert.Equal(BackdropPreset.None, doc.Backdrop);
        Assert.True(doc.SetBackdrop(BackdropPreset.Slate));
        Assert.Equal(BackdropPreset.Slate, doc.Backdrop);
        doc.Undo();
        Assert.Equal(BackdropPreset.None, doc.Backdrop);
        doc.Redo();
        Assert.Equal(BackdropPreset.Slate, doc.Backdrop);
    }

    [Fact]
    public void Annotation_handles_resize_endpoints_and_corners()
    {
        var arrow = new Annotation
        {
            Kind = AnnotationKind.Arrow,
            Points = { new PointD(10, 10), new PointD(50, 10) }
        };
        var arrowHandles = AnnotationHandles.For(arrow);
        Assert.Equal(2, arrowHandles.Count);
        var anchor = AnnotationHandles.AnchorFor(arrow, AnnotationHandleKind.End);
        AnnotationHandles.ApplyResize(arrow, AnnotationHandleKind.End, anchor, new PointD(60, 14), constrain: true);
        Assert.Equal(new PointD(10, 10), arrow.Points[0]);
        Assert.True(Math.Abs(arrow.Points[1].Y - 10) < 0.01);

        var box = new Annotation
        {
            Kind = AnnotationKind.Redact,
            Points = { new PointD(10, 20), new PointD(50, 60) }
        };
        var boxHandles = AnnotationHandles.For(box);
        Assert.Equal(4, boxHandles.Count);
        var nwAnchor = AnnotationHandles.AnchorFor(box, AnnotationHandleKind.NorthWest);
        Assert.Equal(new PointD(50, 60), nwAnchor);
        AnnotationHandles.ApplyResize(box, AnnotationHandleKind.NorthWest, nwAnchor, new PointD(20, 40), constrain: true);
        var (x, y, w, h) = ShapeMath.Bounds(box.Points);
        Assert.Equal(w, h, 2);
        Assert.Equal(50, x + w, 2);
        Assert.Equal(60, y + h, 2);
    }
}
