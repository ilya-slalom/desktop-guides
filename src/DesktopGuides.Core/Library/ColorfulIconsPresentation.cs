namespace DesktopGuides.Core.Library;

/// <summary>The Colorful icons card's copy, toggle state, and automation status.</summary>
public static class ColorfulIconsPresentation
{
    public const string Header = "Colorful icons";
    public const string ToggleName = "Colorful icons";
    public const string Description = "Show navigation and Settings icons in color.";
    public const string Credit = "Icons: Fluent UI System Icons, MIT license.";
    public const string HighContrastNote = "High contrast uses standard icons.";

    public static string CardDescription(bool highContrast) =>
        highContrast ? $"{Description}\n{Credit}\n{HighContrastNote}" : $"{Description}\n{Credit}";

    public static string CardName(bool highContrast) =>
        highContrast ? $"{Header}. {Description} {HighContrastNote}" : $"{Header}. {Description}";

    public static bool ToggleEnabled(bool ready, bool highContrast) => ready && !highContrast;

    public static string Saved(bool on) => on ? "Colorful icons turned on." : "Colorful icons turned off.";

    public static string SaveFailed(string message) => $"Couldn't save the icon setting: {message}";

    public static string Status(int images, int glyphs) => $"images:{images} glyphs:{glyphs}";
}
