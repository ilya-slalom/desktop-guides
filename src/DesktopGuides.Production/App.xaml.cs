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
        shellWindow = new ShellWindow();
        shellWindow.Activate();
        _ = shellWindow.InitializeAsync();
    }

    internal bool ActivateMainWindow()
    {
        if (shellWindow is null || shellWindow.IsClosing)
        {
            return false;
        }
        return ForegroundActivation.Activate(shellWindow);
    }
}
