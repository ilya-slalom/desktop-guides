using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class GuideFileHealthTests
{
    private static readonly Guid GameA = Guid.NewGuid();
    private static readonly Guid GameB = Guid.NewGuid();

    [Fact]
    public void AnUnknownGuideIsOk()
    {
        Assert.Equal(GuideFileStatus.Ok, new GuideFileHealth()[Guid.NewGuid()]);
    }

    [Fact]
    public void MarkRecordsTheStatusAndCountsItForItsGame()
    {
        GuideFileHealth health = new();
        Guid missing = Guid.NewGuid();
        Guid damaged = Guid.NewGuid();
        health.Mark(missing, GameA, GuideFileStatus.Missing);
        health.Mark(damaged, GameA, GuideFileStatus.Damaged);
        health.Mark(Guid.NewGuid(), GameB, GuideFileStatus.Missing);

        Assert.Equal(GuideFileStatus.Missing, health[missing]);
        Assert.Equal(GuideFileStatus.Damaged, health[damaged]);
        Assert.Equal(2, health.CountForGame(GameA));
        Assert.Equal(1, health.CountForGame(GameB));
        Assert.Equal(0, health.CountForGame(Guid.NewGuid()));
    }

    [Fact]
    public void MarkingOkClearsTheStatus()
    {
        GuideFileHealth health = new();
        Guid guide = Guid.NewGuid();
        health.Mark(guide, GameA, GuideFileStatus.Damaged);
        health.Mark(guide, GameA, GuideFileStatus.Ok);

        Assert.Equal(GuideFileStatus.Ok, health[guide]);
        Assert.Equal(0, health.CountForGame(GameA));
    }

    [Fact]
    public void MarkingAgainReplacesTheStatus()
    {
        GuideFileHealth health = new();
        Guid guide = Guid.NewGuid();
        health.Mark(guide, GameA, GuideFileStatus.Missing);
        health.Mark(guide, GameA, GuideFileStatus.Damaged);

        Assert.Equal(GuideFileStatus.Damaged, health[guide]);
        Assert.Equal(1, health.CountForGame(GameA));
    }

    [Fact]
    public void ForgetRemovesTheGuide()
    {
        GuideFileHealth health = new();
        Guid guide = Guid.NewGuid();
        health.Mark(guide, GameA, GuideFileStatus.Missing);
        health.Forget(guide);
        health.Forget(Guid.NewGuid());

        Assert.Equal(GuideFileStatus.Ok, health[guide]);
        Assert.Equal(0, health.CountForGame(GameA));
    }

    [Fact]
    public void ResetReplacesEverythingWithTheMissingList()
    {
        GuideFileHealth health = new();
        Guid stale = Guid.NewGuid();
        Guid missing = Guid.NewGuid();
        health.Mark(stale, GameA, GuideFileStatus.Damaged);
        health.Reset([new MissingGuideFile(missing, GameB)]);

        Assert.Equal(GuideFileStatus.Ok, health[stale]);
        Assert.Equal(GuideFileStatus.Missing, health[missing]);
        Assert.Equal(0, health.CountForGame(GameA));
        Assert.Equal(1, health.CountForGame(GameB));
    }

    [Theory]
    [InlineData(GuideFileStatus.Missing, "File missing")]
    [InlineData(GuideFileStatus.Damaged, "File damaged")]
    public void StatusLabelCopy(GuideFileStatus status, string expected)
    {
        Assert.Equal(expected, GuideFilePresentation.StatusLabel(status));
    }

    [Fact]
    public void OkHasNoStatusLabel()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GuideFilePresentation.StatusLabel(GuideFileStatus.Ok));
    }

    [Theory]
    [InlineData(1, "1 guide needs attention")]
    [InlineData(3, "3 guides need attention")]
    public void AttentionCopy(int count, string expected)
    {
        Assert.Equal(expected, GuideFilePresentation.Attention(count));
    }

    [Theory]
    [InlineData(1, "1 guide record couldn't be read and is hidden. Other guides open normally.")]
    [InlineData(2, "2 guide records couldn't be read and are hidden. Other guides open normally.")]
    public void UnreadableCopy(int count, string expected)
    {
        Assert.Equal(expected, GuideFilePresentation.Unreadable(count));
    }
}
