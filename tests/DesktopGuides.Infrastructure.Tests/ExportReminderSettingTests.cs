using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ExportReminderSettingTests
{
    [Fact]
    public async Task TheReminderStartsNotShown()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        Assert.False((await library.Repository.GetSettingsAsync()).ExportReminderShown);
    }

    [Fact]
    public async Task TheReminderFlagSurvivesARestart()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        await library.Repository.UpdateSettingsAsync(settings => settings with { ExportReminderShown = true });
        await library.RestartAsync();

        Assert.True((await library.Repository.GetSettingsAsync()).ExportReminderShown);
        Assert.Equal("true", library.Scalar("SELECT Value FROM Settings WHERE Key = 'ExportReminderShown'"));
    }

    [Fact]
    public async Task AnUnknownValueReadsAsNotShown()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();
        library.Execute("INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('ExportReminderShown', 'maybe')");

        Assert.False((await library.Repository.GetSettingsAsync()).ExportReminderShown);
    }
}
