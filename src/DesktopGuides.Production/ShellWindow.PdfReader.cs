using DesktopGuides.Core.Library;
using DesktopGuides.Core.Pdf;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Reading;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // The locked guide the panel is asking for, and the render that showed it.
    private Guide? pdfUnlockGuide;
    private int pdfUnlockGeneration = -1;

    // Returns false when a newer render took over, like the TXT path.
    private async Task<bool> OpenPdfGuideAsync(Guide guide, int generation, string? password = null)
    {
        // An unlock attempt keeps the panel up while it checks.
        if (password is null)
        {
            ShowReaderSurface(loading: true);
        }
        readerLoad?.Dispose();
        readerLoad = new CancellationTokenSource();
        CancellationToken token = readerLoad.Token;
        PdfGuideLoad load;
        try
        {
            load = await pdfLoader!.LoadAsync(guide, password, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        if (generation != renderGeneration)
        {
            // No session owns the text source yet.
            (load as PdfGuideLoaded)?.Text.Dispose();
            return false;
        }
        if (load is PdfGuideLoadFailed failed)
        {
            if (failed.Error is PdfGuideLoadError.PasswordRequired or PdfGuideLoadError.PasswordIncorrect)
            {
                ShowPdfUnlock(guide, generation, failed.Error);
            }
            else
            {
                ShowPdfLoadError(failed.Error);
            }
            return true;
        }
        PdfGuideLoaded loaded = (PdfGuideLoaded)load;
        PdfReaderSession session = new(guide, loaded, cacheRoot!, PdfReaderSession.DiagnosticsEnabledForTest());
        // The next render disposes it if this one is cancelled.
        readerSession = session;
        session.Failed += OnPdfSessionFailed;
        session.ZoomChanged += OnPdfZoomChanged;
        session.View.PreviewKeyDown += OnPdfPreviewKeyDown;
        ShowReaderSurface(loading: false, view: session.View);
        try
        {
            await session.OpenAsync(new ManagedGuideSource(guide, loaded.FilePath), password, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (PdfGuideLoadException error)
        {
            if (generation != renderGeneration)
            {
                return false;
            }
            readerSession = null;
            ShowReaderSurface(loading: false);
            await session.DisposeAsync();
            ShowPdfLoadError(error.Error);
            return true;
        }
        if (generation != renderGeneration)
        {
            return false;
        }
        ReaderActions.SetSession(session);
        // SetSession resets these, so they follow it.
        ReaderActions.PageCount = session.PageCount;
        ReaderActions.KeysEnabled = true;
        PdfZoomState zoom = session.ZoomState;
        ReaderActions.SetZoomAvailability(zoom.CanZoomIn, zoom.CanZoomOut);
        bool opened = await OpenAtSavedPlaceAsync(
            guide, session, generation, loaded.ContentSha256, null, token);
        // After an unlock the preview takes focus from the gone password box.
        if (opened && password is not null && ReferenceEquals(readerSession, session))
        {
            FocusPdfPreviewWhenLoaded(session);
        }
        return opened;
    }

    // A small PDF can open before its view has had a layout pass, and an
    // unloaded view can't take focus, so the focus waits for Loaded.
    private void FocusPdfPreviewWhenLoaded(PdfReaderSession session)
    {
        PdfReaderView view = session.View;
        if (view.IsLoaded)
        {
            view.FocusPreview();
            return;
        }
        void OnLoaded(object sender, RoutedEventArgs args)
        {
            view.Loaded -= OnLoaded;
            if (ReferenceEquals(readerSession, session))
            {
                view.FocusPreview();
            }
        }
        view.Loaded += OnLoaded;
    }

    private void ShowPdfLoadError(PdfGuideLoadError error)
    {
        string message = PdfGuideLoadMessages.For(error);
        ShowReaderSurface(loading: false, error: message, action: PdfGuideLoadMessages.ActionFor(error));
        ShowWarningStatus(message);
    }

    private void ShowPdfUnlock(Guide guide, int generation, PdfGuideLoadError error)
    {
        ShowReaderSurface(loading: false);
        pdfUnlockGuide = guide;
        pdfUnlockGeneration = generation;
        PdfUnlockMessage.Text = PdfGuideLoadMessages.For(PdfGuideLoadError.PasswordRequired);
        PdfUnlockPanel.Visibility = Visibility.Visible;
        if (error == PdfGuideLoadError.PasswordIncorrect)
        {
            PdfUnlockError.Text = PdfGuideLoadMessages.For(error);
            PdfUnlockError.Visibility = Visibility.Visible;
            FrameworkElementAutomationPeer.CreatePeerForElement(PdfUnlockError)
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        // The box was cleared before the attempt, so Unlock is disabled
        // (P13); focus goes back to the box for the next try. The panel says
        // what is wrong, so the progress line just hides.
        HideRouteProgress();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (generation == renderGeneration && PdfUnlockPanel.Visibility == Visibility.Visible)
            {
                PdfPasswordInput.Focus(FocusState.Programmatic);
            }
        });
    }

    private void PdfPasswordChanged(object sender, RoutedEventArgs args) =>
        PdfUnlockButton.IsEnabled = PdfPasswordInput.Password.Length > 0;

    // Enter in the box unlocks, like a default button.
    private async void PdfPasswordKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            args.Handled = true;
            await UnlockPdfAsync();
        }
    }

    private async void PdfUnlockClicked(object sender, RoutedEventArgs args) =>
        await UnlockPdfAsync();

    private async Task UnlockPdfAsync()
    {
        if (pdfUnlockGuide is not { } guide || PdfPasswordInput.Password.Length == 0)
        {
            return;
        }
        int generation = pdfUnlockGeneration;
        // Copy the attempt, then clear the box before it runs.
        string password = PdfPasswordInput.Password;
        PdfPasswordInput.Password = string.Empty;
        PdfUnlockError.Visibility = Visibility.Collapsed;
        await RunNavigationAsync(async () =>
        {
            // Back or another guide during the click wins.
            if (generation != renderGeneration || !ReferenceEquals(guide, pdfUnlockGuide))
            {
                return;
            }
            // This attempt consumes the prompt, so one queued behind it can't
            // reopen over its session. A wrong password re-arms it.
            pdfUnlockGuide = null;
            await OpenPdfGuideAsync(guide, generation, password);
        });
    }

    // A late event from a replaced session is ignored.
    private void OnPdfZoomChanged(object? sender, EventArgs args)
    {
        if (sender is PdfReaderSession session && ReferenceEquals(session, readerSession))
        {
            PdfZoomState zoom = session.ZoomState;
            ReaderActions.SetZoomAvailability(zoom.CanZoomIn, zoom.CanZoomOut);
        }
    }

    // P14: a focused preview would page itself, so its keys go to the
    // toolbar first. The page text keeps its own page keys.
    private void OnPdfPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.OriginalSource is TextBox or PasswordBox)
        {
            return;
        }
        args.Handled = ReaderActions.TryRunKey(args.Key, CurrentModifiers(), fromContent: true);
    }

    private static VirtualKeyModifiers CurrentModifiers()
    {
        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;
        if (IsKeyDown(VirtualKey.Control)) modifiers |= VirtualKeyModifiers.Control;
        if (IsKeyDown(VirtualKey.Shift)) modifiers |= VirtualKeyModifiers.Shift;
        if (IsKeyDown(VirtualKey.Menu)) modifiers |= VirtualKeyModifiers.Menu;
        return modifiers;
    }

    private static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    // A late event from a session that a newer render replaced is ignored.
    private async void OnPdfSessionFailed(object? sender, PdfGuideLoadError error)
    {
        await RunNavigationAsync(async () =>
        {
            if (!ReferenceEquals(sender, readerSession))
            {
                return;
            }
            // A failed renderer can't report a place; leave the stored one.
            progressTracking?.Abandon();
            progressTracking = null;
            IReaderSession failed = readerSession!;
            readerSession = null;
            ReaderActions.SetSession(null);
            ShowReaderSurface(loading: false);
            await failed.DisposeAsync();
            ShowPdfLoadError(error);
        });
    }
}
