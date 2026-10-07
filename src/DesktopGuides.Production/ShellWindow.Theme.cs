using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using Microsoft.UI.System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // AccessibilitySettings events need a CoreWindow, which a desktop window lacks.
    private ThemeSettings? themeSettings;
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
        AppThemeChoice.ChoiceChanged += AppThemeChoiceChanged;
        ShellRoot.ActualThemeChanged += (_, _) =>
        {
            UpdateThemeStatus();
            RefreshReaderAppearance();
        };
        // Queued to the UI thread; the event's thread is not documented.
        themeSettings = ThemeSettings.CreateForWindowId(AppWindow.Id);
        themeSettings.Changed += (_, _) =>
            DispatcherQueue.TryEnqueue(() => ApplyTheme(requestedTheme));
    }

    private void ApplyTheme(ThemePreference requested)
    {
        requestedTheme = requested;
        appliedTheme = ThemePresentation.Resolve(requested, themeSettings?.HighContrast == true);
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
        RefreshReaderAppearance();
    }

    // System resolves to the root's theme here, so a reader never reads
    // the Windows theme itself.
    private ReaderTheme ReaderThemeNow() => appliedTheme switch
    {
        _ when themeSettings?.HighContrast == true => ReaderTheme.HighContrast,
        AppliedTheme.Light => ReaderTheme.Light,
        AppliedTheme.Dark => ReaderTheme.Dark,
        _ => ShellRoot.ActualTheme == ElementTheme.Dark ? ReaderTheme.Dark : ReaderTheme.Light
    };

    // Restyles the open guide; TXT and PDF sessions ignore it.
    private async void RefreshReaderAppearance()
    {
        UpdateReaderSurfaceChrome();
        if (readerSession is not IReaderSession session) return;
        try
        {
            await session.ApplyAppearanceAsync(
                new ReaderAppearance(ReaderThemeNow(), readerTextScale), CancellationToken.None);
        }
        catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException)
        {
            // A session closed mid-write has nothing left to style.
        }
    }

    private void UpdateThemeStatus() =>
        AutomationProperties.SetItemStatus(
            AppThemeChoice,
            ThemePresentation.Status(requestedTheme, appliedTheme, ShellRoot.ActualTheme.ToString()));

    private async void AppThemeChoiceChanged(object? sender, EventArgs e)
    {
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
                AnnounceStatus(ThemePresentation.Saved(requested));
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
