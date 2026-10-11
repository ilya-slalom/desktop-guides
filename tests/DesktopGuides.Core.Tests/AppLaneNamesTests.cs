using DesktopGuides.Core.Packaging;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class AppLaneNamesTests
{
    // Every installed smoke opens these exact names, so they must not drift.
    [Fact]
    public void PreviewNamesAreUnchanged()
    {
        AppLaneNames lane = AppLaneNames.Preview;

        Assert.Equal("DesktopGuides.Preview", lane.Prefix);
        Assert.Equal("Desktop Guides Preview", lane.WindowTitle);
        Assert.Equal("DesktopGuides.Preview.Main", lane.InstanceKey);
        Assert.Equal(@"Local\DesktopGuides.Preview.RedirectedActivation", lane.LocalEvent("RedirectedActivation"));
        Assert.Equal(@"Local\DesktopGuides.Preview.Closing.42", lane.LocalEvent("Closing", 42));
        Assert.Equal("DesktopGuides.Preview.Activation.42", lane.PipeName("Activation", 42));
    }

    [Fact]
    public void PublicNamesUseTheReleasePrefix()
    {
        AppLaneNames lane = AppLaneNames.Public;

        Assert.Equal("DesktopGuides", lane.Prefix);
        Assert.Equal("Desktop Guides", lane.WindowTitle);
        Assert.Equal("DesktopGuides.Main", lane.InstanceKey);
        Assert.Equal(@"Local\DesktopGuides.TextLoad.7", lane.LocalEvent("TextLoad", 7));
        Assert.Equal("DesktopGuides.Activation.7", lane.PipeName("Activation", 7));
    }

    [Theory]
    [InlineData(false, "DesktopGuides.Preview")]
    [InlineData(true, "DesktopGuides")]
    public void ForPicksTheLane(bool publicLane, string prefix) =>
        Assert.Equal(prefix, AppLaneNames.For(publicLane).Prefix);

    [Fact]
    public void ThePortableKeyIsShared() =>
        Assert.Equal("DesktopGuides.Portable.Main", AppLaneNames.PortableInstanceKey);

    [Theory]
    [InlineData("")]
    [InlineData("Has.Dot")]
    [InlineData("Has Space")]
    [InlineData(@"Back\Slash")]
    public void ANameMustBeOneSegment(string name)
    {
        Assert.Throws<ArgumentException>(() => AppLaneNames.Preview.LocalEvent(name));
        Assert.Throws<ArgumentException>(() => AppLaneNames.Preview.PipeName(name, 1));
    }
}
