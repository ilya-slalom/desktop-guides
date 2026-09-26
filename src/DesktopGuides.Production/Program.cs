using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;

namespace DesktopGuides.Production;

internal static class Program
{
    private static AppInstance? primaryInstance;
    private static DispatcherQueue? uiQueue;
    private static App? app;

    [STAThread]
    private static async Task Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Trace("Main started");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Trace("ProcessExit");

        AppInstance current = AppInstance.GetCurrent();
        AppActivationArguments activation = current.GetActivatedEventArgs();
        AppInstance main = AppInstance.FindOrRegisterForKey("DesktopGuides.Preview.Main");
        Trace($"FindOrRegisterForKey IsCurrent={main.IsCurrent}");
        if (!main.IsCurrent)
        {
            Trace("RedirectActivationToAsync started");
            await main.RedirectActivationToAsync(activation);
            Trace("RedirectActivationToAsync completed");
            return;
        }

        primaryInstance = main;
        primaryInstance.Activated += (_, _) =>
        {
            Trace("Redirected activation received");
            Volatile.Read(ref uiQueue)?.TryEnqueue(
                () =>
                {
                    Trace("Activating main window");
                    app?.ActivateMainWindow();
                    Trace("Main window activated");
                });
        };

        Application.Start(initialization =>
        {
            Trace("Application.Start callback");
            DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            Volatile.Write(ref uiQueue, dispatcher);
            app = new App();
        });
        Trace("Application.Start returned");
    }

    internal static void Trace(string message)
    {
        try
        {
            string root = ApplicationData.Current.LocalFolder.Path;
            if (!File.Exists(Path.Combine(root, "enable-lifecycle-trace")))
            {
                return;
            }
            File.AppendAllText(Path.Combine(root, "shell-lifecycle.txt"),
                $"{DateTimeOffset.UtcNow:o} {Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch
        {
            // Tracing must not change app startup or shutdown.
        }
    }
}
