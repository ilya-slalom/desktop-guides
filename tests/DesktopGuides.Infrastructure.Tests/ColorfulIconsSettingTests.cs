using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ColorfulIconsSettingTests
{
    [Fact]
    public async Task ColorfulIconsStartOff()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        Assert.False((await library.Repository.GetSettingsAsync()).ColorfulIcons);
    }

    [Fact]
    public async Task TheChoiceSurvivesARestart()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = true });
        await library.RestartAsync();

        Assert.True((await library.Repository.GetSettingsAsync()).ColorfulIcons);
        Assert.Equal("true", library.Scalar("SELECT Value FROM Settings WHERE Key = 'ColorfulIcons'"));
    }

    [Fact]
    public async Task TurningItOffAgainStoresFalse()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();

        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = true });
        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = false });

        Assert.False((await library.Repository.GetSettingsAsync()).ColorfulIcons);
        Assert.Equal("false", library.Scalar("SELECT Value FROM Settings WHERE Key = 'ColorfulIcons'"));
    }

    [Fact]
    public async Task AnUnknownValueReadsAsOff()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();
        library.Execute("INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('ColorfulIcons', '1')");

        Assert.False((await library.Repository.GetSettingsAsync()).ColorfulIcons);
    }

    [Fact]
    public async Task TheToggleKeepsTheOtherSettings()
    {
        await using RemovalLibrary library = await RemovalLibrary.CreateAsync();
        await library.Repository.UpdateSettingsAsync(settings => settings with
        {
            Theme = ThemePreference.Dark,
            WindowMaterial = WindowMaterial.Acrylic,
            ExportReminderShown = true
        });

        await library.Repository.UpdateSettingsAsync(settings => settings with { ColorfulIcons = true });
        await library.RestartAsync();

        AppSettings stored = await library.Repository.GetSettingsAsync();
        Assert.Equal(ThemePreference.Dark, stored.Theme);
        Assert.Equal(WindowMaterial.Acrylic, stored.WindowMaterial);
        Assert.True(stored.ExportReminderShown);
        Assert.True(stored.ColorfulIcons);
    }
}
