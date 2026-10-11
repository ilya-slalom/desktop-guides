using DesktopGuides.Core.Library;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class ColorfulIconsPresentationTests
{
    [Fact]
    public void TheCardDescriptionCreditsTheIconSet()
    {
        Assert.Equal(
            "Show navigation and Settings icons in color.\nIcons: Fluent UI System Icons, MIT license.",
            ColorfulIconsPresentation.CardDescription(highContrast: false));
    }

    [Fact]
    public void InHighContrastTheCardAddsTheNote()
    {
        Assert.Equal(
            "Show navigation and Settings icons in color.\nIcons: Fluent UI System Icons, MIT license.\n" +
            "High contrast uses standard icons.",
            ColorfulIconsPresentation.CardDescription(highContrast: true));
    }

    [Theory]
    [InlineData(false, "Colorful icons. Show navigation and Settings icons in color.")]
    [InlineData(true, "Colorful icons. Show navigation and Settings icons in color. High contrast uses standard icons.")]
    public void TheCardNameIsHeaderAndDescriptionWithoutTheCredit(bool highContrast, string expected)
    {
        Assert.Equal(expected, ColorfulIconsPresentation.CardName(highContrast));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void TheToggleIsDisabledInHighContrast(bool ready, bool highContrast, bool enabled)
    {
        Assert.Equal(enabled, ColorfulIconsPresentation.ToggleEnabled(ready, highContrast));
    }

    [Fact]
    public void TheCopyIsVerbatim()
    {
        Assert.Equal("Colorful icons", ColorfulIconsPresentation.Header);
        Assert.Equal("Colorful icons", ColorfulIconsPresentation.ToggleName);
        Assert.Equal("Colorful icons turned on.", ColorfulIconsPresentation.Saved(true));
        Assert.Equal("Colorful icons turned off.", ColorfulIconsPresentation.Saved(false));
        Assert.Equal("Couldn't save the icon setting: disk full",
            ColorfulIconsPresentation.SaveFailed("disk full"));
    }

    [Fact]
    public void TheStatusCountsImagesAndGlyphs()
    {
        Assert.Equal("images:10 glyphs:0", ColorfulIconsPresentation.Status(10, 0));
        Assert.Equal("images:0 glyphs:10", ColorfulIconsPresentation.Status(0, 10));
    }
}
