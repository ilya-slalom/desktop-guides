using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GameDetailsTests
{
    [Fact]
    public void TrimsUnicodeTitleAndNormalizesEmptyOptionalFields()
    {
        GameDetails details = GameDetails.Create(
            "  大航海時代  ", "  PC  ", " \n ");

        Assert.Equal("大航海時代", details.Title);
        Assert.Equal("PC", details.Platform);
        Assert.Null(details.Notes);
    }

    [Fact]
    public void EnforcesSchemaLengthsAfterTrimming()
    {
        Assert.Equal(GameDetails.TitleLimit,
            GameDetails.Create(" " + new string('T', 160) + " ", null, null)
                .Title.Length);
        Assert.Throws<ArgumentException>(() =>
            GameDetails.Create(" \t ", null, null));
        Assert.Throws<ArgumentException>(() =>
            GameDetails.Create(new string('T', 161), null, null));
        Assert.Throws<ArgumentException>(() =>
            GameDetails.Create("Title", new string('P', 81), null));
        Assert.Throws<ArgumentException>(() =>
            GameDetails.Create("Title", null, new string('N', 2001)));
    }
}
