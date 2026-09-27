using Microsoft.UI.Xaml;

namespace DesktopGuides.ReaderToolbarSmoke;

public partial class App : Application
{
    private Window? mainWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        mainWindow = new ToolbarWindow();
        mainWindow.Activate();
    }
}
