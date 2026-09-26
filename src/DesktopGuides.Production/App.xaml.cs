using Microsoft.UI.Xaml;

namespace DesktopGuides.Production;

public partial class App : Application
{
    private ShellWindow? shellWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Program.Trace("OnLaunched");
        shellWindow = new ShellWindow();
        shellWindow.Activate();
        _ = shellWindow.InitializeAsync();
    }

    internal void ActivateMainWindow() => shellWindow?.Activate();
}
