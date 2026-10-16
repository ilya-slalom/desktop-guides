using System.Text.Json;
using DesktopGuides.Infrastructure.Reading;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Reading;

public sealed class HtmlOpenTimelineTests
{
    private long now;

    [Fact]
    public void StagesKeepTheirOrderAndTimeSinceTheOpenBegan()
    {
        HtmlOpenTimeline timeline = new(() => now);
        now = 1_000;
        timeline.Begin();
        timeline.Mark("load");
        now = 1_250;
        timeline.Mark("environment-created");
        now = 1_400;
        timeline.Mark("ensured", "browser 4321");

        using JsonDocument json = JsonDocument.Parse(timeline.ToJson());

        Assert.Equal(1, json.RootElement.GetProperty("opens").GetInt32());
        JsonElement[] stages = [.. json.RootElement.GetProperty("stages").EnumerateArray()];
        Assert.Equal(["load", "environment-created", "ensured"], stages.Select(s => s.GetProperty("stage").GetString()));
        Assert.Equal([0L, 250L, 400L], stages.Select(s => s.GetProperty("ms").GetInt64()));
        Assert.Equal("browser 4321", stages[2].GetProperty("detail").GetString());
        Assert.False(stages[0].TryGetProperty("detail", out _));
    }

    // The file holds the latest open: an earlier open's stages would read as
    // this one's progress.
    [Fact]
    public void ANewOpenStartsAFreshList()
    {
        HtmlOpenTimeline timeline = new(() => now);
        timeline.Begin();
        timeline.Mark("load");
        timeline.Mark("ready");
        now = 9_000;
        timeline.Begin();
        now = 9_100;
        timeline.Mark("load");

        using JsonDocument json = JsonDocument.Parse(timeline.ToJson());

        Assert.Equal(2, json.RootElement.GetProperty("opens").GetInt32());
        JsonElement stage = Assert.Single(json.RootElement.GetProperty("stages").EnumerateArray());
        Assert.Equal(100, stage.GetProperty("ms").GetInt64());
    }

    [Fact]
    public void ADetailWithQuotesStaysValidJson()
    {
        HtmlOpenTimeline timeline = new(() => now);
        timeline.Begin();
        timeline.Mark("failed", "COMException \"0x8007139F\"");

        using JsonDocument json = JsonDocument.Parse(timeline.ToJson());

        Assert.Equal("COMException \"0x8007139F\"",
            json.RootElement.GetProperty("stages")[0].GetProperty("detail").GetString());
    }
}
