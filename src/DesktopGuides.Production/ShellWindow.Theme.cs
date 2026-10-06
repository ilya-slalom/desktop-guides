using DesktopGuides.Core.Library;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private bool applyingThemeSelection;

    private void InitializeThemeChoice() =>
        AppThemeChoice.SetOptions(
            ThemePresentation.Options
                .Select(option => new BoundedChoiceOption(option.Label, option.AutomationId))
                .ToList());

    private void ShowTheme(ThemePreference requested)
    {
        applyingThemeSelection = true;
        AppThemeChoice.SelectedIndex = ThemePresentation.IndexOf(requested);
        applyingThemeSelection = false;
    }
}
