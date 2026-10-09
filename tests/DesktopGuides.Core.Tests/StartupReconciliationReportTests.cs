using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class StartupReconciliationReportTests
{
    private static readonly Guid Guide = Guid.NewGuid();
    private static readonly Guid Game = Guid.NewGuid();

    [Fact]
    public void NoMissingGuidesIsAnEmptyList()
    {
        StartupReconciliationReport report = new(0, 0);

        Assert.Empty(report.MissingGuides);
        Assert.Equal(report, new StartupReconciliationReport(0, 0, MissingGuides: []));
    }

    [Fact]
    public void ReportsWithTheSameMissingGuidesAreEqual()
    {
        StartupReconciliationReport first = new(1, 2, 3, [new MissingGuideFile(Guide, Game)], 4);
        StartupReconciliationReport second = new(1, 2, 3, [new MissingGuideFile(Guide, Game)], 4);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ADifferentMissingGuideOrUnreadableCountIsNotEqual()
    {
        StartupReconciliationReport report = new(0, 0, MissingGuides: [new MissingGuideFile(Guide, Game)]);

        Assert.NotEqual(report, new StartupReconciliationReport(0, 0));
        Assert.NotEqual(report, report with { MissingGuides = [new MissingGuideFile(Guid.NewGuid(), Game)] });
        Assert.NotEqual(report, report with { UnreadableGuideCount = 1 });
    }
}
