using System.Text;

namespace SnapKit.Tests;

public class MspFeaturesTests
{
    [Fact]
    public void Sensitive_detector_finds_emails_ips_phones_tokens_and_labeled_secrets()
    {
        var words = new List<OcrWordBox>
        {
            // Row 1: Email + IP
            new("Contact:", 10, 10, 48, 12),
            new("alex.rivera@acmecorp.io", 64, 10, 140, 12),
            new("Host:", 220, 10, 30, 12),
            new("192.168.10.42:8443", 256, 10, 110, 12),
            // Row 2: Split phone number
            new("Phone:", 10, 34, 40, 12),
            new("(555)", 56, 34, 32, 12),
            new("234-5678", 92, 34, 54, 12),
            // Row 3: Known API token + safe version string (0.4.3 must NOT be flagged!)
            new("Key:", 10, 58, 28, 12),
            new("sk_live_51Nx92Kp4a8f10b7c2", 44, 58, 160, 12),
            new("v0.4.3", 240, 58, 38, 12),
            // Row 4: Labeled password
            new("Password:", 10, 82, 58, 12),
            new("Winter2026!", 74, 82, 72, 12),
            // Row 5: Normal text (must NOT be flagged)
            new("Server", 10, 106, 40, 12),
            new("Healthy", 56, 106, 45, 12),
        };

        var matches = SensitiveTextDetector.Find(words);

        Assert.Equal(5, matches.Count);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Email);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.IpAddress);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.Phone);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.SecretToken);
        Assert.Contains(matches, m => m.Kind == SensitiveKind.LabeledSecret);
    }

    [Fact]
    public void Sensitive_detector_checks_luhn_on_credit_cards_and_ignores_random_ids()
    {
        var words = new List<OcrWordBox>
        {
            // Valid Luhn test card split into 4 groups
            new("4111", 10, 10, 28, 12),
            new("1111", 42, 10, 28, 12),
            new("1111", 74, 10, 28, 12),
            new("1111", 106, 10, 28, 12),
            // Invalid Luhn 16-digit ID
            new("4111111111111112", 10, 36, 110, 12),
        };

        var matches = SensitiveTextDetector.Find(words);

        Assert.Single(matches);
        Assert.Equal(SensitiveKind.CreditCard, matches[0].Kind);
    }

    [Fact]
    public void MultiShot_composer_measures_side_by_side_and_vertical_cleanly()
    {
        var sizes = new[] { (800, 500), (800, 500) };

        var side = MultiShotComposer.Measure(sizes, MultiShotLayout.SideBySide, padding: 20, gap: 16, labelHeight: 28);
        Assert.Equal(20 + 800 + 16 + 800 + 20, side.TotalWidth);
        Assert.Equal(20 + 28 + 500 + 20, side.TotalHeight);
        Assert.Equal(2, side.Slots.Count);
        Assert.Equal(20, side.Slots[0].ImageRect.X);
        Assert.Equal(836, side.Slots[1].ImageRect.X);

        var vert = MultiShotComposer.Measure(sizes, MultiShotLayout.Vertical, padding: 20, gap: 16, labelHeight: 28);
        Assert.Equal(840, vert.TotalWidth);
        Assert.Equal(20 + (28 + 500) + 16 + (28 + 500) + 20, vert.TotalHeight);
        Assert.Equal(48, vert.Slots[0].ImageRect.Y);
        Assert.Equal(592, vert.Slots[1].ImageRect.Y);
    }

    [Fact]
    public void StepGuideHtml_escapes_html_and_computes_exact_cf_html_byte_offsets()
    {
        var items = new[]
        {
            new StepGuideExportItem(1, "Click <Settings> & \"Save\"", new byte[] { 137, 80, 78, 71 }, 640, 400),
            new StepGuideExportItem(2, "Confirm MFA", new byte[] { 137, 80, 78, 71 }, 640, 400),
        };

        string doc = StepGuideHtml.BuildDocument("Reset <Outlook>", items);
        Assert.Contains("Reset &lt;Outlook&gt;", doc);
        Assert.Contains("Click &lt;Settings&gt; &amp; &quot;Save&quot;", doc);

        string cf = StepGuideHtml.BuildClipboardCfHtml("Reset Outlook", items);
        byte[] utf8 = Encoding.UTF8.GetBytes(cf);
        int startHtml = int.Parse(cf.Split("StartHTML:")[1][..10]);
        int endHtml = int.Parse(cf.Split("EndHTML:")[1][..10]);
        int startFrag = int.Parse(cf.Split("StartFragment:")[1][..10]);
        int endFrag = int.Parse(cf.Split("EndFragment:")[1][..10]);

        Assert.Equal(utf8.Length, endHtml);
        Assert.True(startHtml < startFrag && startFrag < endFrag && endFrag < endHtml);
        Assert.StartsWith("<html><body><!--StartFragment-->", Encoding.UTF8.GetString(utf8, startHtml, endHtml - startHtml));
    }

    [Fact]
    public void TicketStamp_and_batch_annotations_support_undo_redo()
    {
        var doc = new ShotDocument(new PixelBuffer(200, 120));
        Assert.Equal("INC-4821  ·  2026-11-24 06:15", TicketStamp.Format(new DateTime(2026, 11, 24, 6, 15, 0), "INC-4821", "HV3"));
        Assert.Equal("2026-11-24 06:15  ·  HV3", TicketStamp.Format(new DateTime(2026, 11, 24, 6, 15, 0), "", "HV3"));

        doc.SetStamp(true, "INC-4821");
        Assert.True(doc.ShowStamp);
        Assert.Equal("INC-4821", doc.StampNote);

        int added = doc.AddAnnotations(new[]
        {
            new Annotation { Kind = AnnotationKind.Blur, Points = new() { new(10, 10), new(50, 30) } },
            new Annotation { Kind = AnnotationKind.Blur, Points = new() { new(60, 10), new(100, 30) } }
        });
        Assert.Equal(2, added);
        Assert.Equal(2, doc.Annotations.Count);

        // Single Undo removes the entire batch cleanly
        doc.Undo();
        Assert.Empty(doc.Annotations);
        Assert.True(doc.ShowStamp);

        // Next Undo reverts Stamp
        doc.Undo();
        Assert.False(doc.ShowStamp);

        // Magnifier exposes 4 corner handles
        var mag = new Annotation { Kind = AnnotationKind.Magnifier, Points = new() { new(20, 20), new(100, 100) } };
        Assert.Equal(4, AnnotationHandles.For(mag).Count);
    }

    [Fact]
    public void PrintLayout_fits_standard_shot_on_one_page_and_paginates_tall_guides()
    {
        // Standard 16:9 capture on US Letter portrait (816x1056 DIPs at 96 DPI) -> 1 centered page
        var single = PrintLayout.Paginate(1920, 1080, printableWidth: 816, printableHeight: 1056, margin: 36);
        Assert.Single(single);
        Assert.Equal(0, single[0].SourceY);
        Assert.Equal(1080, single[0].SourceHeight);
        Assert.True(single[0].DestWidth <= 816 - 72 + 0.01);
        Assert.True(single[0].DestHeight <= 1056 - 72 + 0.01);

        // Tall 860x3200 multi-step client guide -> slices cleanly across multiple pages with full pixel coverage
        var multi = PrintLayout.Paginate(860, 3200, printableWidth: 816, printableHeight: 1056, margin: 36);
        Assert.True(multi.Count >= 3);
        Assert.Equal(0, multi[0].SourceY);
        Assert.Equal(3200, multi.Sum(p => p.SourceHeight));
        for (int i = 1; i < multi.Count; i++)
            Assert.Equal(multi[i - 1].SourceY + multi[i - 1].SourceHeight, multi[i].SourceY);
    }
}
