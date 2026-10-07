using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ThemePresentationTests
{
    [Fact]
    public void OptionsAreSystemLightDarkInOrder()
    {
        Assert.Equal(
            [
                new ThemeOption(ThemePreference.System, "System", "ThemeSystem"),
                new ThemeOption(ThemePreference.Light, "Light", "ThemeLight"),
                new ThemeOption(ThemePreference.Dark, "Dark", "ThemeDark")
            ],
            ThemePresentation.Options);
    }

    [Theory]
    [InlineData(ThemePreference.System, 0)]
    [InlineData(ThemePreference.Light, 1)]
    [InlineData(ThemePreference.Dark, 2)]
    [InlineData((ThemePreference)99, 0)]
    public void IndexOfFindsTheOption(ThemePreference preference, int index) =>
        Assert.Equal(index, ThemePresentation.IndexOf(preference));

    [Theory]
    [InlineData(ThemePreference.System, AppliedTheme.FollowSystem)]
    [InlineData(ThemePreference.Light, AppliedTheme.Light)]
    [InlineData(ThemePreference.Dark, AppliedTheme.Dark)]
    [InlineData((ThemePreference)99, AppliedTheme.FollowSystem)]
    public void WithoutHighContrastThePreferenceApplies(
        ThemePreference preference, AppliedTheme applied) =>
        Assert.Equal(applied, ThemePresentation.Resolve(preference, highContrast: false));

    [Theory]
    [InlineData(ThemePreference.System)]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void HighContrastAlwaysFollowsSystem(ThemePreference preference) =>
        Assert.Equal(AppliedTheme.FollowSystem, ThemePresentation.Resolve(preference, highContrast: true));

    [Theory]
    [InlineData(ThemePreference.Light, AppliedTheme.Light, "Light", "Light")]
    [InlineData(ThemePreference.Dark, AppliedTheme.Dark, "Dark", "Dark")]
    [InlineData(ThemePreference.System, AppliedTheme.FollowSystem, "Light", "System (Light)")]
    [InlineData(ThemePreference.System, AppliedTheme.FollowSystem, "Dark", "System (Dark)")]
    [InlineData(ThemePreference.Dark, AppliedTheme.FollowSystem, "Light", "Dark (high contrast)")]
    public void StatusNamesTheAppliedTheme(
        ThemePreference preference, AppliedTheme applied, string actual, string expected) =>
        Assert.Equal(expected, ThemePresentation.Status(preference, applied, actual));

    [Fact]
    public void SavedNamesTheChoice() =>
        Assert.Equal("App theme set to Dark.", ThemePresentation.Saved(ThemePreference.Dark));

    [Fact]
    public void SaveFailedCarriesTheMessage() =>
        Assert.Equal("Couldn't save the app theme: database is locked",
            ThemePresentation.SaveFailed("database is locked"));
}
