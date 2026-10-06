using DesktopGuides.Core.Library;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    private readonly AccessibilitySettings accessibility = new();
    private readonly SemaphoreSlim themeSaveGate = new(1, 1);
    private bool applyingThemeSelection;
    // The choice on screen, and the last one storage accepted.
    private ThemePreference requestedTheme = ThemePreference.System;
    private ThemePreference committedTheme = ThemePreference.System;
    private AppliedTheme appliedTheme = AppliedTheme.FollowSystem;

    private void InitializeThemeChoice()
    {
        AppThemeChoice.SetOptions(
            ThemePresentation.Options
                .Select(option => new BoundedChoiceOption(option.Label, option.AutomationId))
                .ToList());
        // System follows Windows, so its status follows the resolved theme.
        ShellRoot.ActualThemeChanged += (_, _) => UpdateThemeStatus();
        // Raised off the UI thread.
        accessibility.HighContrastChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ApplyTheme(requestedTheme));
    }

    private void ApplyTheme(ThemePreference requested)
    {
        requestedTheme = requested;
        appliedTheme = ThemePresentation.Resolve(requested, accessibility.HighContrast);
        ElementTheme element = appliedTheme switch
        {
            AppliedTheme.Light => ElementTheme.Light,
            AppliedTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        ShellRoot.RequestedTheme = element;
        DialogTheme = element;
        ReaderActions.DialogTheme = element;
        TitleBarTheme titleBar = appliedTheme switch
        {
            AppliedTheme.Light => TitleBarTheme.Light,
            AppliedTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode
        };
        AppWindow.TitleBar.PreferredTheme = titleBar;
        AutomationProperties.SetItemStatus(AppTitleBar, titleBar.ToString());
        applyingThemeSelection = true;
        AppThemeChoice.SelectedIndex = ThemePresentation.IndexOf(requested);
        applyingThemeSelection = false;
        UpdateThemeStatus();
    }

    private void UpdateThemeStatus() =>
        AutomationProperties.SetItemStatus(
            AppThemeChoice,
            ThemePresentation.Status(requestedTheme, appliedTheme, ShellRoot.ActualTheme.ToString()));

    private async void AppThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A cleared selection is put back by BoundedChoice.
        if (applyingThemeSelection || repository is null || AppThemeChoice.SelectedIndex < 0)
        {
            return;
        }
        ThemePreference requested = ThemePresentation.Options[AppThemeChoice.SelectedIndex].Preference;
        if (requested == requestedTheme)
        {
            return;
        }
        ApplyTheme(requested);
        // Saves commit in the order the user chose.
        await themeSaveGate.WaitAsync();
        try
        {
            await repository.UpdateSettingsAsync(s => s with { Theme = requested });
            committedTheme = requested;
            if (requested == requestedTheme)
            {
                ShowTransientStatus(ThemePresentation.Saved(requested));
            }
        }
        catch (Exception error)
        {
            // A newer choice is already on screen; its own save decides.
            if (requested == requestedTheme)
            {
                ApplyTheme(committedTheme);
            }
            ShowErrorStatus(ThemePresentation.SaveFailed(error.Message));
        }
        finally
        {
            themeSaveGate.Release();
        }
    }
}
