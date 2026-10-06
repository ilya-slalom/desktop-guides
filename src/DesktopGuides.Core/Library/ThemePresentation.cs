namespace DesktopGuides.Core.Library;

/// <summary>The theme the shell applies, independent of WinUI types.</summary>
public enum AppliedTheme
{
    FollowSystem,
    Light,
    Dark
}

public sealed record ThemeOption(ThemePreference Preference, string Label, string AutomationId);

/// <summary>The App theme choice's options, resolution, and status copy.</summary>
public static class ThemePresentation
{
    public static IReadOnlyList<ThemeOption> Options { get; } =
    [
        new(ThemePreference.System, "System", "ThemeSystem"),
        new(ThemePreference.Light, "Light", "ThemeLight"),
        new(ThemePreference.Dark, "Dark", "ThemeDark")
    ];

    public static int IndexOf(ThemePreference preference) =>
        Math.Max(0, Options.ToList().FindIndex(option => option.Preference == preference));

    // Windows high contrast takes precedence over the stored preference.
    public static AppliedTheme Resolve(ThemePreference preference, bool highContrast) =>
        highContrast ? AppliedTheme.FollowSystem : preference switch
        {
            ThemePreference.Light => AppliedTheme.Light,
            ThemePreference.Dark => AppliedTheme.Dark,
            _ => AppliedTheme.FollowSystem
        };

    // The installed checks read this as the choice's ItemStatus.
    public static string Status(ThemePreference preference, AppliedTheme applied, string actualTheme) =>
        applied != AppliedTheme.FollowSystem ? Label(preference)
            : preference is ThemePreference.Light or ThemePreference.Dark
                ? $"{Label(preference)} (high contrast)"
                : $"System ({actualTheme})";

    public static string Saved(ThemePreference preference) => $"App theme set to {Label(preference)}.";

    public static string SaveFailed(string message) => $"Could not save the app theme: {message}";

    private static string Label(ThemePreference preference) => Options[IndexOf(preference)].Label;
}
