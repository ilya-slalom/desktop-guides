using DesktopGuides.Core.Reading;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class TextBoxOffsetsTests
{
    [Fact]
    public void LineFeedsMapOneToOne() =>
        Assert.Equal((2, 1), TextBoxOffsets.Map("a\nb", new TextMatch(2, 1)));

    [Fact]
    public void EachEarlierCarriageReturnLineFeedIsOneCharacter() =>
        Assert.Equal((4, 1), TextBoxOffsets.Map("x\r\ny\r\nz", new TextMatch(6, 1)));

    [Fact]
    public void AMatchAcrossALineBreakLosesOneCharacter() =>
        Assert.Equal((0, 9), TextBoxOffsets.Map("end.\r\nNext", new TextMatch(0, 10)));

    [Fact]
    public void ALoneCarriageReturnIsOneCharacter() =>
        Assert.Equal((2, 1), TextBoxOffsets.Map("a\rb", new TextMatch(2, 1)));
}
