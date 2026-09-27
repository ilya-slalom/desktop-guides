using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Production;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.ReaderToolbarSmoke;

// Test-only host for the linked production ReaderToolbar.
public sealed class ToolbarWindow : Window
{
    private readonly FakeReaderSession session = new();
    private readonly ReaderToolbar toolbar = new()
    {
        Width = 680,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private readonly TextBlock lastAction = new()
    {
        Text = "No action",
        FontSize = 18
    };

    public ToolbarWindow()
    {
        Title = "Reader toolbar test";
        AutomationProperties.SetAutomationId(lastAction, "LastReaderAction");
        session.ActionExecuted += action => lastAction.Text = Describe(action);
        toolbar.CommandFailed += message => lastAction.Text = message;
        toolbar.SetSession(session);

        StackPanel controls = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        controls.Children.Add(ControlButton("Text controls", "TextControls",
            async () => await ChangeCapabilitiesAsync(ReaderCapabilities.TextSize |
                ReaderCapabilities.Find)));
        controls.Children.Add(ControlButton("All controls", "AllControls",
            async () => await ChangeCapabilitiesAsync(
                ReaderCapabilities.PageNavigation |
                ReaderCapabilities.PageJump |
                ReaderCapabilities.FitWidth |
                ReaderCapabilities.Zoom |
                ReaderCapabilities.TextSize |
                ReaderCapabilities.Find)));
        controls.Children.Add(ControlButton("No controls", "NoControls",
            async () => await ChangeCapabilitiesAsync(ReaderCapabilities.None)));
        controls.Children.Add(ControlButton("Detach reader", "DetachReader",
            () =>
            {
                toolbar.SetSession(null);
                session.SetCapabilities(ReaderCapabilities.PageNavigation);
                return Task.CompletedTask;
            }));
        controls.Children.Add(ControlButton("Narrow toolbar", "NarrowToolbar",
            () =>
            {
                toolbar.Width = 180;
                return Task.CompletedTask;
            }));
        controls.Children.Add(ControlButton("Wide toolbar", "WideToolbar",
            () =>
            {
                toolbar.Width = 680;
                return Task.CompletedTask;
            }));

        StackPanel content = new()
        {
            Padding = new Thickness(24),
            Spacing = 20
        };
        content.Children.Add(new TextBlock
        {
            Text = "Reader toolbar test",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(controls);
        content.Children.Add(toolbar);
        content.Children.Add(lastAction);
        Content = content;
    }

    private async Task ChangeCapabilitiesAsync(ReaderCapabilities capabilities)
    {
        await Task.Run(() => session.SetCapabilities(capabilities));
    }

    private static Button ControlButton(
        string text, string id, Func<Task> action)
    {
        Button button = new() { Content = text };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static string Describe(ReaderAction action) => action switch
    {
        PageTurnAction page => $"Page turn {page.Delta}",
        PageJumpAction page => $"Page jump {page.PageNumber}",
        FitWidthAction => "Fit to width",
        ZoomAction zoom => $"Zoom {zoom.Factor:G}",
        TextSizeAction text => $"Text size {text.Factor:G}",
        FindAction find => $"Find {find.Query}",
        _ => action.Command.ToString()
    };

    private sealed class FakeReaderSession : IReaderSession
    {
        private int capabilities;

        public GuideFormat Format => GuideFormat.Pdf;
        public ReaderCapabilities Capabilities =>
            (ReaderCapabilities)Volatile.Read(ref capabilities);
        public event EventHandler? CapabilitiesChanged;
        public event EventHandler<LocationChangedEventArgs>? LocationChanged
        {
            add { }
            remove { }
        }
        public event Action<ReaderAction>? ActionExecuted;

        public void SetCapabilities(ReaderCapabilities value)
        {
            Volatile.Write(ref capabilities, (int)value);
            CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task OpenAsync(ManagedGuideSource source, CancellationToken token) =>
            Task.CompletedTask;

        public Task<ReaderLocation> GetLocationAsync(CancellationToken token) =>
            throw new NotSupportedException();

        public Task<RestoreOutcome> RestoreLocationAsync(
            ReaderLocation location, CancellationToken token) =>
            throw new NotSupportedException();

        public Task ApplyAppearanceAsync(
            ReaderAppearance appearance, CancellationToken token) =>
            Task.CompletedTask;

        public Task ExecuteAsync(ReaderAction action, CancellationToken token)
        {
            ActionExecuted?.Invoke(action);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
