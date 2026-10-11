using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T11.5: the choice on screen, and the last one storage accepted.
    private bool colorfulIcons;
    private bool committedColorfulIcons;
    private bool iconsReady;
    private bool applyingIconToggle;
    private readonly SemaphoreSlim iconSaveGate = new(1, 1);

    private void InitializeIcons()
    {
        ColorfulIconsSettingsCard.Header = ColorfulIconsPresentation.Header;
        AutomationProperties.SetName(ColorfulIconsToggle, ColorfulIconsPresentation.ToggleName);
        // The built-in Settings item exists only once the template has loaded.
        Navigation.Loaded += (_, _) => ApplyIcons();
        ApplyIcons();
    }

    // Sets every host from the catalog; high contrast always gets glyphs.
    private void ApplyIcons()
    {
        bool highContrast = themeSettings?.HighContrast == true;
        int images = 0;
        int glyphs = 0;
        AppIconChoice Pick(AppIcon icon)
        {
            AppIconChoice choice = AppIconCatalog.For(icon, colorfulIcons, highContrast);
            if (choice.IsAsset) images++; else glyphs++;
            return choice;
        }

        LibraryItem.Icon = AppIcons.Element(Pick(AppIcon.NavLibrary));
        if (Navigation.SettingsItem is NavigationViewItem settingsItem)
        {
            settingsItem.Icon = AppIcons.Element(Pick(AppIcon.NavSettings));
        }
        AppTitleBar.IconSource = AppIcons.Source(Pick(AppIcon.TitleBar));
        ShowEmptyStateIcon(LibraryEmptyGlyph, LibraryEmptyImage, Pick(AppIcon.EmptyLibrary));
        ShowEmptyStateIcon(LibraryNoResultsGlyph, LibraryNoResultsImage, Pick(AppIcon.EmptySearch));
        AppThemeSettingsCard.HeaderIcon = AppIcons.Element(Pick(AppIcon.ThemeCard));
        WindowMaterialSettingsCard.HeaderIcon = AppIcons.Element(Pick(AppIcon.MaterialCard));
        ExportSettingsCard.HeaderIcon = AppIcons.Element(Pick(AppIcon.BackupCard));
        RestoreSettingsExpander.HeaderIcon = AppIcons.Element(Pick(AppIcon.RestoreCard));
        ProviderSettings.SetHeaderIcon(AppIcons.Element(Pick(AppIcon.ProvidersCard)));

        applyingIconToggle = true;
        ColorfulIconsToggle.IsOn = colorfulIcons;
        applyingIconToggle = false;
        ColorfulIconsToggle.IsEnabled = ColorfulIconsPresentation.ToggleEnabled(iconsReady, highContrast);
        ColorfulIconsSettingsCard.Description = ColorfulIconsPresentation.CardDescription(highContrast);
        AutomationProperties.SetName(
            ColorfulIconsSettingsCard, ColorfulIconsPresentation.CardName(highContrast));
        // Ruling 10: the hosts are Raw, so the smoke reads what was set here.
        AutomationProperties.SetItemStatus(
            ColorfulIconsToggle, ColorfulIconsPresentation.Status(images, glyphs));
    }

    private static void ShowEmptyStateIcon(FontIcon glyph, ImageIcon image, AppIconChoice choice)
    {
        if (choice.IsAsset)
        {
            image.Source = AppIcons.Image(choice.AssetFile!);
        }
        else
        {
            glyph.Glyph = choice.Glyph!;
        }
        glyph.Visibility = choice.IsAsset ? Visibility.Collapsed : Visibility.Visible;
        image.Visibility = choice.IsAsset ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void ColorfulIconsToggled(object sender, RoutedEventArgs e)
    {
        if (applyingIconToggle || repository is null)
        {
            return;
        }
        bool requested = ColorfulIconsToggle.IsOn;
        if (requested == colorfulIcons)
        {
            return;
        }
        colorfulIcons = requested;
        ApplyIcons();
        // Saves commit in the order the user chose, as the theme's do.
        await iconSaveGate.WaitAsync();
        try
        {
            await repository.UpdateSettingsAsync(s => s with { ColorfulIcons = requested });
            committedColorfulIcons = requested;
            if (requested == colorfulIcons)
            {
                AnnounceStatus(ColorfulIconsPresentation.Saved(requested));
            }
        }
        catch (Exception error)
        {
            // A newer choice is already on screen; its own save decides.
            if (requested == colorfulIcons)
            {
                colorfulIcons = committedColorfulIcons;
                ApplyIcons();
            }
            ShowErrorStatus(ColorfulIconsPresentation.SaveFailed(error.Message));
        }
        finally
        {
            iconSaveGate.Release();
        }
    }
}
