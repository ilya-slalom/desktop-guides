using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DesktopGuides.Production;

internal static class Program
{
    private static AppInstance? primaryInstance;
    private static DispatcherQueue? uiQueue;

    [STAThread]
    private static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        AppInstance current = AppInstance.GetCurrent();
        AppActivationArguments activation = current.GetActivatedEventArgs();
        AppInstance main = AppInstance.FindOrRegisterForKey("DesktopGuides.Preview.Main");
        if (!main.IsCurrent)
        {
            await main.RedirectActivationToAsync(activation);
            return;
        }

        primaryInstance = main;
        primaryInstance.Activated += (_, _) =>
        {
            Volatile.Read(ref uiQueue)?.TryEnqueue(
                () => (Application.Current as App)?.ActivateMainWindow());
        };

        Application.Start(initialization =>
        {
            DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            Volatile.Write(ref uiQueue, dispatcher);
            _ = new App();
        });
    }
}
