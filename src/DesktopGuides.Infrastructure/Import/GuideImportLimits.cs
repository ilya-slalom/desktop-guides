using DesktopGuides.Core.Maps;

namespace DesktopGuides.Infrastructure.Import;

public sealed record GuideImportLimits(
    long MaxTxtBytes = 64L * 1024 * 1024,
    long MaxPdfBytes = 1024L * 1024 * 1024,
    StaticHtmlScanLimits? Html = null,
    long MaxMapBytes = MapImageLimits.MaxBytes)
{
    public StaticHtmlScanLimits HtmlLimits => Html ?? new StaticHtmlScanLimits();
}
