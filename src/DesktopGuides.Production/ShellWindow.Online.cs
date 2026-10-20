using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Core.Online;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Production.Materials;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using WinRT.Interop;

namespace DesktopGuides.Production;

// S22: Find online. The reader browses a supported site in the in-app
// browser, as the site serves it, and saves a guide: its own pages are
// captured one at a time through the same session and saved to the game.
public sealed partial class ShellWindow
{
    private static readonly IReadOnlyDictionary<SourceSite, IGuideSourceAdapter> OnlineAdapters =
        new Dictionary<SourceSite, IGuideSourceAdapter>
        {
            [SourceSite.GameFaqs] = new GameFaqsAdapter(),
            [SourceSite.Neoseeker] = new NeoseekerAdapter(),
        };

    private OnlineBrowser? onlineBrowser;
    private WebViewPageLoader? onlineLoader;
    private CaptureStore? captureStore;
    private CancellationTokenSource? onlineCapture;
    private Task? onlineCaptureRun;
    private CaptureDraft? onlineDraft;
    private GuidePageReading? onlineReading;
    private Guid onlineGameId;
    private string onlineGameTitle = "";
    private bool onlineSourceSuppressed;

    private CaptureStore Captures => captureStore ??=
        new CaptureStore(Path.Combine(cacheRoot!, "online-captures"), TimeProvider.System);

    // Installed tests only: an unhandled exception's details, which the
    // Application log doesn't keep, for the harness to collect.
    private bool crashesRecorded;

    private void RecordCrashesForTest()
    {
#if !PUBLIC_LANE
        if (crashesRecorded || cacheRoot is not string root ||
            !TestGate.IsOpen(AppLane.Current.LocalEvent("CrashDiagnostics", Environment.ProcessId)))
        {
            return;
        }
        crashesRecorded = true;
        Application.Current.UnhandledException += (_, args) =>
        {
            try
            {
                string folder = Path.Combine(root, "diagnostics");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, $"unhandled-{Environment.ProcessId}.txt"), args.Exception.ToString());
            }
            catch (Exception)
            {
            }
        };
#endif
    }

    private void InitializeOnline()
    {
        // Find online is available whenever the game page can import.
        ImportGuideButton.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) =>
            FindOnlineButton.IsEnabled = ImportGuideButton.IsEnabled && OnlineSources.Available.Count > 0);
        AddShortcut(VirtualKey.L, ShortcutCommand.FocusAddress, FocusOnlineAddress);
        AddShortcut(VirtualKey.S, ShortcutCommand.SaveOnline, SaveOnlineFromKeyboard);
        ShowShortcut(OnlineAddress, "Address", ShortcutCommand.FocusAddress);
        ShowShortcut(OnlineSave, "Save to library", ShortcutCommand.SaveOnline);
        foreach (OnlineSource source in OnlineSources.Available) OnlineSource.Items.Add(source.Name);
    }

    private async void FindOnlineClicked(object sender, RoutedEventArgs args)
    {
        if (navigator.Current is not GameRoute game) return;
        await RunNavigationAsync(async () =>
        {
            navigator.OpenOnline(game.GameId);
            await RenderCurrentAsync();
        });
    }

    private async Task ShowOnlineAsync(SqliteLibraryRepository library, Guid gameId, int generation)
    {
        OnlinePanel.Visibility = Visibility.Visible;
        HideStatus();
        Game? game = await library.GetGameAsync(gameId);
        if (generation != renderGeneration) return;
        onlineGameId = gameId;
        onlineGameTitle = game?.Title ?? "this game";
        OnlineHeading.Text = $"Find online: {onlineGameTitle}";
        OnlineStatus.Text = "";
        OnlineInfo.IsOpen = false;
        OnlineInfo.Visibility = Visibility.Collapsed;
        if (onlineBrowser is null)
        {
            try
            {
                onlineBrowser = await OnlineBrowser.CreateAsync(
                    cacheRoot!, WindowNative.GetWindowHandle(this), OnlineFixtures());
            }
            catch (Exception)
            {
                OnlineStatus.Text = "The in-app browser couldn't start. Repair the WebView2 Runtime, then try again.";
                return;
            }
            onlineBrowser.PageLoaded += (_, _) => _ = UpdateOnlineSaveAsync();
            onlineBrowser.ExternalLinkRequested += (_, url) => _ = ExternalLinkLaunchers.Create(cacheRoot!).LaunchAsync(url);
            OnlineBrowserHost.Child = onlineBrowser.View;
        }
        SelectOnlineSource(OnlineSources.Available[0]);
        onlineBrowser.Navigate(OnlineSources.StartUrl(OnlineSources.Available[0], onlineGameTitle));
        if (Captures.ListForGame(gameId).FirstOrDefault() is { } waiting)
        {
            ShowWaitingDraft(waiting);
        }
        OnlineAddress.Focus(FocusState.Programmatic);
        AnnounceStatus("Find online ready.");
    }

    private OnlineFixtureRoutes? OnlineFixtures()
    {
#if !PUBLIC_LANE
        // Installed tests only: pages come from fixtures, not the network.
        string manifest = Path.Combine(libraryPaths!.DataRoot, "test", "online", "manifest.json");
        if (TestGate.IsOpen(AppLane.Current.LocalEvent("OnlineFixtures", Environment.ProcessId)) && File.Exists(manifest))
        {
            return OnlineFixtureRoutes.Load(manifest);
        }
#endif
        return null;
    }

    private async Task CloseOnlineAsync()
    {
        // The capture stops before its browser goes; its pages stay in the draft.
        onlineCapture?.Cancel();
        if (onlineCaptureRun is { } run)
        {
            try
            {
                await run;
            }
            catch (Exception)
            {
                // Reported by the capture itself.
            }
        }
        onlineLoader?.Dispose();
        onlineLoader = null;
        if (onlineBrowser is not null)
        {
            OnlineBrowserHost.Child = null;
            await onlineBrowser.DisposeAsync();
            onlineBrowser = null;
        }
        onlineReading = null;
        OnlinePanel.Visibility = Visibility.Collapsed;
    }

    private void SelectOnlineSource(OnlineSource source)
    {
        onlineSourceSuppressed = true;
        OnlineSource.SelectedIndex = OnlineSources.Available.ToList().IndexOf(source);
        onlineSourceSuppressed = false;
    }

    private void OnlineSourceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (onlineSourceSuppressed || onlineBrowser is null || OnlineSource.SelectedIndex < 0) return;
        onlineBrowser.Navigate(OnlineSources.StartUrl(OnlineSources.Available[OnlineSource.SelectedIndex], onlineGameTitle));
    }

    private void OnlineAddressKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter) return;
        args.Handled = true;
        GoToOnlineAddress();
    }

    private void OnlineGoClicked(object sender, RoutedEventArgs args) => GoToOnlineAddress();

    private void GoToOnlineAddress()
    {
        if (onlineBrowser is null) return;
        string text = OnlineAddress.Text.Trim();
        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? url) || OnlineSources.Find(url) is not { } source)
        {
            OnlineStatus.Text = "Desktop Guides can save guides from " +
                string.Join(" and ", OnlineSources.Available.Select(available => available.Name)) +
                ". Open other sites in your browser.";
            return;
        }
        SelectOnlineSource(source);
        onlineBrowser.Navigate(url);
    }

    // After each page loads: is it a guide this app can save?
    private async Task UpdateOnlineSaveAsync(bool keepStatus = false)
    {
        if (onlineBrowser?.CurrentUrl is not Uri url) return;
        if (!ReferenceEquals(FocusManager.GetFocusedElement(OnlineAddress.XamlRoot), OnlineAddress))
        {
            OnlineAddress.Text = url.AbsoluteUri;
        }
        if (OnlineSources.Find(url) is { } source) SelectOnlineSource(source);
        if (onlineCapture is not null) return;
        GuidePageReading? reading = null;
        if (OnlineSources.Find(url) is { } site && OnlineAdapters.TryGetValue(site.Site, out IGuideSourceAdapter? adapter))
        {
            string html = await onlineBrowser.GetHtmlAsync();
            reading = adapter.Read(url, html);
        }
        onlineReading = reading;
        OnlineSave.IsEnabled = reading is not null;
        if (!keepStatus)
        {
            OnlineStatus.Text = reading is null ? "This page isn't a guide Desktop Guides can save." : "";
        }
    }

    private bool FocusOnlineAddress() => OnlineAddress.Focus(FocusState.Keyboard);

    private bool SaveOnlineFromKeyboard()
    {
        if (!OnlineSave.IsEnabled) return false;
        OnlineSaveClicked(OnlineSave, new RoutedEventArgs());
        return true;
    }

    private async void OnlineSaveClicked(object sender, RoutedEventArgs args)
    {
        // One dialog at a time: a second click while one is open or closing does nothing.
        if (onlineReading is not { } reading || onlineCapture is not null || DialogOpen()) return;
        if (NoticeRules.Classify(reading.Notice) == NoticeKind.Restrictive && !await ConfirmNoticeAsync(reading.Notice!))
        {
            return;
        }
        CaptureDraft draft = Captures.Find(onlineGameId, reading.Match.Site, reading.Match.ItemId)
            ?? Captures.Begin(onlineGameId, reading);
        await StartCaptureAsync(draft);
    }

    private Task StartCaptureAsync(CaptureDraft draft) => onlineCaptureRun = RunCaptureAsync(draft);

    private async Task<bool> ConfirmNoticeAsync(string notice)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Navigation.XamlRoot,
            Title = "This guide's notice limits copies",
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock { Text = notice, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            },
            PrimaryButtonText = "Save for personal reading",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // Another dialog is still closing; nothing is saved.
            return false;
        }
    }

    private async Task RunCaptureAsync(CaptureDraft draft)
    {
        if (onlineBrowser is null) return;
        onlineDraft = draft;
        onlineCapture = new CancellationTokenSource();
        CancellationToken token = onlineCapture.Token;
        OnlineInfo.IsOpen = false;
        OnlineInfo.Visibility = Visibility.Collapsed;
        OnlineSave.IsEnabled = false;
        OnlineProgressRow.Visibility = Visibility.Visible;
        ShowCaptureProgress(draft.Captured.Count, draft.Pages.Count);
        try
        {
            onlineLoader ??= await onlineBrowser.CreateLoaderAsync();
            GuideCapture capture = new(OnlineAdapters[draft.Item.Site], onlineLoader, TimeProvider.System);
            CaptureOutcome outcome = await capture.RunAsync(
                draft, new Progress<(int Done, int Total)>(step => ShowCaptureProgress(step.Done, step.Total)), token);
            if (outcome is CaptureOutcome.Paused paused)
            {
                ShowPaused(paused);
                return;
            }
            await SaveDraftAsync(draft, allowFormatChange: false);
        }
        catch (OperationCanceledException)
        {
            OnlineStatus.Text = "Saving stopped. The pages saved so far are kept.";
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            OnlineStatus.Text = "";
            ShowOnlineInfo(InfoBarSeverity.Error, "Couldn't save the guide",
                "A page couldn't be loaded. The pages saved so far are kept.",
                showPage: null, canSave: draft.Captured.Count > 0);
        }
        finally
        {
            onlineCapture.Dispose();
            onlineCapture = null;
            OnlineProgressRow.Visibility = Visibility.Collapsed;
            // A page that loaded during the capture wasn't read; read what's showing now,
            // so Save never offers the guide just saved for another page.
            onlineReading = null;
            OnlineSave.IsEnabled = false;
            if (onlineBrowser is not null) _ = UpdateOnlineSaveAsync(keepStatus: true);
        }
    }

    private void ShowCaptureProgress(int done, int total)
    {
        int next = Math.Min(done + 1, total);
        OnlineStatus.Text = $"Saving page {next} of {total}…";
        OnlineProgress.Maximum = Math.Max(total, 1);
        OnlineProgress.Value = done;
        AutomationProperties.SetName(OnlineProgress, OnlineStatus.Text);
    }

    private void ShowPaused(CaptureOutcome.Paused paused)
    {
        string site = OnlineSources.Available.FirstOrDefault(source => source.Site == paused.Draft.Item.Site)?.Name ?? "The site";
        // What the site answered: a refusal's status, or no answer at all.
        string answer = paused.Status switch
        {
            0 => " (no answer)",
            200 => "",
            int status => $" (HTTP {status})",
        };
        OnlineStatus.Text = "";
        ShowOnlineInfo(InfoBarSeverity.Warning, "Saving paused",
            $"{site} didn't return page {paused.Number} of {paused.Total}{answer}. It may be asking you to confirm you're a person.",
            showPage: paused.Page.Url, canSave: paused.Draft.Captured.Count > 0);
    }

    private void ShowWaitingDraft(CaptureDraft draft)
    {
        onlineDraft = draft;
        string title = draft.Captured.FirstOrDefault()?.Reading.Title ?? draft.Pages[0].Title ?? "a guide";
        ShowOnlineInfo(InfoBarSeverity.Informational, "",
            $"An unfinished save of {title} is waiting ({draft.Captured.Count} of {draft.TotalPages} pages).",
            showPage: null, canSave: draft.Captured.Count > 0);
    }

    private Uri? onlinePausedPage;

    private void ShowOnlineInfo(InfoBarSeverity severity, string title, string message, Uri? showPage, bool canSave)
    {
        onlinePausedPage = showPage;
        OnlineInfo.Severity = severity;
        OnlineInfo.Title = title;
        OnlineInfo.Message = message;
        AutomationProperties.SetName(OnlineInfo, title.Length > 0 ? $"{title}. {message}" : message);
        OnlineShowPage.Visibility = showPage is null ? Visibility.Collapsed : Visibility.Visible;
        OnlineSavePartial.IsEnabled = canSave;
        OnlineInfo.Visibility = Visibility.Visible;
        OnlineInfo.IsOpen = true;
        AnnounceStatus(OnlineInfo.Message);
    }

    private void OnlineShowPageClicked(object sender, RoutedEventArgs args)
    {
        if (onlinePausedPage is { } page) onlineBrowser?.Navigate(page);
    }

    private async void OnlineResumeClicked(object sender, RoutedEventArgs args)
    {
        if (onlineDraft is { } draft && onlineCapture is null) await StartCaptureAsync(draft);
    }

    private async void OnlineSavePartialClicked(object sender, RoutedEventArgs args)
    {
        if (onlineDraft is { } draft && onlineCapture is null)
        {
            OnlineInfo.IsOpen = false;
            OnlineInfo.Visibility = Visibility.Collapsed;
            await SaveDraftAsync(draft, allowFormatChange: false);
        }
    }

    private void OnlineDiscardClicked(object sender, RoutedEventArgs args)
    {
        if (onlineCapture is not null) return;
        onlineDraft?.Discard();
        onlineDraft = null;
        OnlineInfo.IsOpen = false;
        OnlineInfo.Visibility = Visibility.Collapsed;
        OnlineStatus.Text = "The unfinished save was discarded.";
    }

    private void OnlineCancelClicked(object sender, RoutedEventArgs args) => onlineCapture?.Cancel();

    private async Task SaveDraftAsync(CaptureDraft draft, bool allowFormatChange, bool asNew = false)
    {
        if (repository is not SqliteLibraryRepository library || guidePublisher is not { } publisher) return;
        OnlineGuideSaver saver = new(library, publisher, importValidator,
            Path.Combine(cacheRoot!, "online-work"), TimeProvider.System);
        string title = draft.Captured.FirstOrDefault()?.Reading.Title ?? "The guide";
        OnlineSaveResult result;
        try
        {
            OnlineStatus.Text = "Saving to your library…";
            result = asNew
                ? await saver.SaveAsNewAsync(draft, null, CancellationToken.None)
                : await saver.SaveAsync(draft, allowFormatChange, null, CancellationToken.None);
        }
        catch (GuideImportException error)
        {
            OnlineStatus.Text = "";
            ShowOnlineInfo(InfoBarSeverity.Error, "Couldn't save the guide", error.Message, showPage: null, canSave: true);
            return;
        }
        switch (result)
        {
            case OnlineSaveResult.NeedsFormatChange change:
                OnlineStatus.Text = "";
                await AskFormatChangeAsync(draft, change);
                return;
            case OnlineSaveResult.Added added:
                ShowOnlineDone($"Saved {title} to {onlineGameTitle}.", added.GuideId);
                break;
            case OnlineSaveResult.Replaced replaced:
                ShowOnlineDone($"Replaced {title}. Your place is kept.", replaced.GuideId);
                break;
            case OnlineSaveResult.UpToDate same:
                ShowOnlineDone($"{title} is up to date.", same.GuideId);
                break;
        }
        onlineDraft = null;
        await RemindToExportAsync();
    }

    private async Task AskFormatChangeAsync(CaptureDraft draft, OnlineSaveResult.NeedsFormatChange change)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Navigation.XamlRoot,
            Title = "Replace the saved guide?",
            Content = new TextBlock
            {
                Text = $"The saved copy is a {ImportPresentation.FormatLabel(change.Saved)} guide and the new one is " +
                       $"{ImportPresentation.FormatLabel(change.New)}. Replacing it loses your place and bookmarks.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Replace",
            SecondaryButtonText = "Keep both",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
        switch (await dialog.ShowAsync())
        {
            case ContentDialogResult.Primary:
                await SaveDraftAsync(draft, allowFormatChange: true);
                break;
            case ContentDialogResult.Secondary:
                await SaveDraftAsync(draft, allowFormatChange: false, asNew: true);
                break;
            default:
                ShowWaitingDraft(draft);
                break;
        }
    }

    private void ShowOnlineDone(string message, Guid guideId)
    {
        OnlineStatus.Text = message;
        ShowStatus(message, InfoBarSeverity.Success, true, false);
        ShowStatusAction("Open guide", () => OpenGuideAsync(guideId));
    }

    // Settings: sign out of every site the in-app browser visited.
    private async void ClearOnlineDataClicked(object sender, RoutedEventArgs args)
    {
        ContentDialog dialog = new()
        {
            XamlRoot = Navigation.XamlRoot,
            Title = "Clear online data",
            Content = "Sign out of every site and delete cookies the in-app browser stored?",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        DialogSurface.Apply(dialog, EffectiveMaterial, DialogTheme);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || cacheRoot is null) return;
        await CloseOnlineAsync();
        try
        {
            string folder = Path.Combine(cacheRoot, "Online");
            if (Directory.Exists(folder)) await Task.Run(() => Directory.Delete(folder, recursive: true));
            AnnounceStatus("Online data cleared.");
            ShowTransientStatus("Online data cleared.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowErrorStatus("Couldn't clear online data. Close Desktop Guides, then try again.");
        }
    }
}
