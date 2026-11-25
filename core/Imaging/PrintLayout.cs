namespace SnapKit;

public readonly record struct PrintPageSlice(
    int PageIndex,
    int SourceY,
    int SourceHeight,
    double DestX,
    double DestY,
    double DestWidth,
    double DestHeight);

/// <summary>
/// Computes single-page fit or automatic multi-page vertical pagination for printing
/// standard screenshots, tall scrolling captures, and multi-step guides cleanly.
/// </summary>
public static class PrintLayout
{
    public static IReadOnlyList<PrintPageSlice> Paginate(
        int imageWidth,
        int imageHeight,
        double printableWidth,
        double printableHeight,
        double margin = 36.0)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || printableWidth <= 1 || printableHeight <= 1)
            return Array.Empty<PrintPageSlice>();

        double safeMarginX = Math.Min(margin, printableWidth * 0.12);
        double safeMarginY = Math.Min(margin, printableHeight * 0.12);
        double usableW = Math.Max(1.0, printableWidth - safeMarginX * 2.0);
        double usableH = Math.Max(1.0, printableHeight - safeMarginY * 2.0);

        // Width-fit scale (never upscale small captures past 1.0x at 96 DIPs so tiny dialogs stay crisp)
        double fitWidthScale = Math.Min(1.0, usableW / imageWidth);
        double scaledHeightAtWidth = imageHeight * fitWidthScale;

        // If the image fits comfortably on 1 page (or within 18% of 1 page height), scale to fit a single page cleanly.
        if (scaledHeightAtWidth <= usableH * 1.18)
        {
            double singleScale = Math.Min(1.0, Math.Min(usableW / imageWidth, usableH / imageHeight));
            double dw = imageWidth * singleScale;
            double dh = imageHeight * singleScale;
            double dx = safeMarginX + (usableW - dw) * 0.5;
            double dy = safeMarginY + (usableH - dh) * 0.5;
            return new[] { new PrintPageSlice(0, 0, imageHeight, dx, dy, dw, dh) };
        }

        // Tall scrolling capture or multi-step client guide: paginate vertically at full readable width!
        int sliceSourceH = Math.Max(1, (int)Math.Floor(usableH / fitWidthScale));
        var pages = new List<PrintPageSlice>();
        int y = 0;
        int pageIndex = 0;
        while (y < imageHeight)
        {
            int h = Math.Min(sliceSourceH, imageHeight - y);
            double dw = imageWidth * fitWidthScale;
            double dh = h * fitWidthScale;
            double dx = safeMarginX + (usableW - dw) * 0.5;
            pages.Add(new PrintPageSlice(pageIndex++, y, h, dx, safeMarginY, dw, dh));
            y += h;
        }
        return pages;
    }
}
