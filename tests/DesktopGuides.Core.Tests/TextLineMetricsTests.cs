using System.Text;
using DesktopGuides.Core.Text;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextLineMetricsTests
{
    private static TextGuideDocument Document(string text) =>
        TextGuideDocument.Decode(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void MaxColumnsCountsTabExpansion()
    {
        Assert.Equal(17, TextLineMetrics.MaxColumns(Document("ab\n\t\tx\nabc"), CancellationToken.None));
    }

    [Fact]
    public void AnEmptyDocumentHasNoColumns()
    {
        Assert.Equal(0, TextLineMetrics.MaxColumns(Document(""), CancellationToken.None));
    }

    [Fact]
    public void ACancelledTokenThrows()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            TextLineMetrics.MaxColumns(Document("a\nb"), cancel.Token));
    }
}
