using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DesktopGuides.Production;

internal static class Program
{
    private static AppInstance? primaryInstance;
    private static DispatcherQueue? uiQueue;
    private static App? app;

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleObjects(
        uint flags, uint timeoutMilliseconds, uint handleCount,
        IntPtr[] handles, out uint index);

    [STAThread]
    private static void Main(string[] args)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("WinUI requires an STA entry point.");
        }
        WinRT.ComWrappersSupport.InitializeComWrappers();

        AppInstance current = AppInstance.GetCurrent();
        AppActivationArguments activation = current.GetActivatedEventArgs();
        AppInstance main = AppInstance.FindOrRegisterForKey("DesktopGuides.Preview.Main");
        if (!main.IsCurrent)
        {
            RedirectActivation(activation, main);
            return;
        }

        primaryInstance = main;
        primaryInstance.Activated += (_, _) =>
        {
            Volatile.Read(ref uiQueue)?.TryEnqueue(
                () => app?.ActivateMainWindow());
        };

        Application.Start(initialization =>
        {
            DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(dispatcher));
            Volatile.Write(ref uiQueue, dispatcher);
            app = new App();
        });
    }

    private static void RedirectActivation(
        AppActivationArguments activation, AppInstance main)
    {
        using EventWaitHandle completed = new(false, EventResetMode.ManualReset);
        Task redirect = Task.Run(async () =>
            await main.RedirectActivationToAsync(activation));
        _ = redirect.ContinueWith(_ =>
        {
            try
            {
                completed.Set();
            }
            catch (ObjectDisposedException)
            {
                // A timed-out redirect no longer has a waiting entry point.
            }
        }, TaskScheduler.Default);

        int result = CoWaitForMultipleObjects(
            0, 30_000, 1, [completed.SafeWaitHandle.DangerousGetHandle()],
            out _);
        if (result != 0)
        {
            throw new TimeoutException(
                $"Activation redirection failed or timed out: 0x{result:X8}.");
        }
        redirect.GetAwaiter().GetResult();
    }
}
