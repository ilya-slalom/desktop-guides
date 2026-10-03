namespace DesktopGuides.Core.Pdf;

public readonly record struct PdfRasterWidth(int Width)
{
    public static PdfRasterWidth PageTooLarge => new(0);
    public bool IsTooLarge => Width < 1;
}

public static class PdfRasterBudget
{
    public const long MaxBytes = 100_663_296;
    private const int Step = 64;
    private const int MinWidth = 64;
    private const int MaxWidth = 4096;

    // aspectRatio is page height over width. Widths snap to 64 so small
    // resizes reuse cached images.
    public static PdfRasterWidth WidthFor(double displayPixels, double aspectRatio, long maxBytes)
    {
        if (!double.IsFinite(displayPixels) || displayPixels <= 0 ||
            !double.IsFinite(aspectRatio) || aspectRatio <= 0 || maxBytes <= 0)
        {
            return PdfRasterWidth.PageTooLarge;
        }
        int width = (int)Math.Clamp(Math.Ceiling(displayPixels / Step) * Step, MinWidth, MaxWidth);
        // In doubles, so an extreme ratio can't overflow.
        while ((double)width * Math.Ceiling(width * aspectRatio) * 4 > maxBytes)
        {
            width /= 2;
            if (width < 1) return PdfRasterWidth.PageTooLarge;
        }
        return new PdfRasterWidth(width);
    }
}
