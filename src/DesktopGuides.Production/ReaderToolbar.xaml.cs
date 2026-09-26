using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ReaderToolbar : UserControl
{
    private IReaderSession? session;
    private CancellationTokenSource sessionActions = new();

    public ReaderToolbar()
    {
        InitializeComponent();
    }

    public event Action<string>? CommandFailed;

    public void SetSession(IReaderSession? value)
    {
        if (ReferenceEquals(session, value))
        {
            return;
        }

        if (session is not null)
        {
            session.CapabilitiesChanged -= CapabilitiesChanged;
        }
        sessionActions.Cancel();
        sessionActions.Dispose();
        sessionActions = new CancellationTokenSource();
        session = value;
        if (session is not null)
        {
            session.CapabilitiesChanged += CapabilitiesChanged;
        }
        RefreshCommands();
    }

    private void CapabilitiesChanged(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, session))
        {
            return;
        }
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshCommands();
        }
        else
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(sender, session))
                {
                    RefreshCommands();
                }
            });
        }
    }

    private void RefreshCommands()
    {
        HashSet<ReaderCommand> supported = session is null
            ? []
            : ReaderCommandPolicy.VisibleCommands(session).ToHashSet();
        bool pages = supported.Contains(ReaderCommand.PageTurn);
        bool textSize = supported.Contains(ReaderCommand.TextSize);
        bool zoom = supported.Contains(ReaderCommand.Zoom);
        bool pageJump = supported.Contains(ReaderCommand.PageJump);
        bool fitWidth = supported.Contains(ReaderCommand.FitWidth);
        bool find = supported.Contains(ReaderCommand.Find);
        PreviousPage.Visibility = Show(pages);
        NextPage.Visibility = Show(pages);
        SmallerText.Visibility = Show(textSize);
        LargerText.Visibility = Show(textSize);
        ZoomOut.Visibility = Show(zoom);
        ZoomIn.Visibility = Show(zoom);
        GoToPage.Visibility = Show(pageJump);
        FitToWidth.Visibility = Show(fitWidth);
        FindInGuide.Visibility = Show(find);
        Commands.Visibility = Show(
            pages || textSize || zoom || pageJump || fitWidth || find);
    }

    private static Visibility Show(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    private async Task ExecuteAsync(ReaderAction action, string description)
    {
        IReaderSession? current = session;
        if (current is null)
        {
            return;
        }
        CancellationToken token = sessionActions.Token;
        try
        {
            await ReaderCommandPolicy.ExecuteAsync(current, action, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A new guide replaced the reader while its command was running.
        }
        catch (Exception error)
        {
            if (ReferenceEquals(current, session))
            {
                CommandFailed?.Invoke($"Could not {description}: {error.Message}");
            }
        }
    }

    private async Task<string?> PromptAsync(
        string title, string label, string primaryButtonText)
    {
        TextBox input = new()
        {
            Header = label
        };
        AutomationProperties.SetAutomationId(input, "ReaderCommandInput");
        ContentDialog dialog = new()
        {
            Title = title,
            Content = input,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary
            ? input.Text.Trim()
            : null;
    }

    private async void PreviousPageClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new PageTurnAction(-1), "turn to the previous page");

    private async void NextPageClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new PageTurnAction(1), "turn to the next page");

    private async void SmallerTextClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new TextSizeAction(0.9), "make the text smaller");

    private async void LargerTextClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new TextSizeAction(1.1), "make the text larger");

    private async void ZoomOutClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new ZoomAction(0.9), "zoom out");

    private async void ZoomInClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new ZoomAction(1.1), "zoom in");

    private async void FitToWidthClicked(object sender, RoutedEventArgs args) =>
        await ExecuteAsync(new FitWidthAction(), "fit to width");

    private async void GoToPageClicked(object sender, RoutedEventArgs args)
    {
        IReaderSession? current = session;
        string? value = await PromptAsync("Go to page", "Page number", "Go");
        if (value is null || !ReferenceEquals(current, session))
        {
            return;
        }
        if (!int.TryParse(value, out int page) || page < 1)
        {
            CommandFailed?.Invoke("Enter a page number greater than zero.");
            return;
        }
        await ExecuteAsync(new PageJumpAction(page), "go to that page");
    }

    private async void FindInGuideClicked(object sender, RoutedEventArgs args)
    {
        IReaderSession? current = session;
        string? query = await PromptAsync("Find in guide", "Search text", "Find");
        if (query is null || !ReferenceEquals(current, session))
        {
            return;
        }
        if (query.Length == 0)
        {
            CommandFailed?.Invoke("Enter text to find.");
            return;
        }
        await ExecuteAsync(new FindAction(query), "find that text");
    }
}
