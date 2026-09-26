using DesktopGuides.Infrastructure.Activation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace DesktopGuides.Production;

internal static class Program
{
    private const string InstanceKey = "DesktopGuides.Preview.Main";
    private const string ActivationProbeName =
        @"Local\DesktopGuides.Preview.RedirectedActivation";
    private const string RedirectSelectedProbeName =
        @"Local\DesktopGuides.Preview.RedirectSelected";
    private const string RedirectContinueProbeName =
        @"Local\DesktopGuides.Preview.RedirectContinue";
    private const string ActivationQueuedProbeName =
        @"Local\DesktopGuides.Preview.ActivationQueued";
    private const string ActivationContinueProbeName =
        @"Local\DesktopGuides.Preview.ActivationContinue";
    private const string AcceptanceReceivedProbeName =
        @"Local\DesktopGuides.Preview.AcceptanceReceived";
    private const string AcceptanceContinueProbeName =
        @"Local\DesktopGuides.Preview.AcceptanceContinue";
    private static AppInstance? primaryInstance;
    private static EventWaitHandle? closingSignal;
    private static LaunchActivationPipe? activationPipe;
    private static DispatcherQueue? uiQueue;
    private static App? app;

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
        if (activation.Kind != ExtendedActivationKind.Launch)
        {
            throw new NotSupportedException(
                $"This preview shell does not handle {activation.Kind} activation.");
        }
        AppInstance? main = FindPrimaryOrActivate();
        if (main is null)
        {
            return;
        }

        primaryInstance = main;
        try
        {
            closingSignal = new EventWaitHandle(
                false, EventResetMode.ManualReset,
                ClosingSignalName(Environment.ProcessId));
            closingSignal.Reset();
            activationPipe = new LaunchActivationPipe(
                ActivationPipeName(Environment.ProcessId),
                request => Volatile.Read(ref uiQueue)?.TryEnqueue(
                    async () =>
                    {
                        await PauseQueuedActivationForTestAsync();
                        if (app?.ActivateMainWindow() == true)
                        {
                            if (request.Complete(true))
                            {
                                SignalActivationProbe();
                            }
                        }
                        else
                        {
                            request.Complete(false);
                        }
                    }) == true);

            Application.Start(initialization =>
            {
                DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(dispatcher));
                Volatile.Write(ref uiQueue, dispatcher);
                app = new App();
            });
        }
        finally
        {
            ReleaseInstanceKey();
            activationPipe?.Dispose();
            activationPipe = null;
            closingSignal?.Dispose();
            closingSignal = null;
        }
    }

    internal static void ReleaseInstanceKey()
    {
        closingSignal?.Set();
        activationPipe?.Stop();
        primaryInstance?.UnregisterKey();
        primaryInstance = null;
    }

    private static AppInstance? FindPrimaryOrActivate()
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            AppInstance target = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (target.IsCurrent)
            {
                return target;
            }
            PauseAfterSelectingTargetForTest();
            if (IsClosing(target))
            {
                Thread.Sleep(50);
                continue;
            }
            if (LaunchActivationPipe.TryRequest(
                ActivationPipeName((int)target.ProcessId), deadline))
            {
                PauseAfterAcceptanceForTest();
                return null;
            }
            Thread.Sleep(50);
        }
        throw new TimeoutException(
            "Could not activate or take over the application window.");
    }

    private static string ClosingSignalName(int processId) =>
        $@"Local\DesktopGuides.Preview.Closing.{processId}";

    private static string ActivationPipeName(int processId) =>
        $"DesktopGuides.Preview.Activation.{processId}";

    private static void PauseAfterSelectingTargetForTest()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                RedirectSelectedProbeName, out EventWaitHandle? selected))
            {
                return;
            }
            using (selected)
            {
                if (!EventWaitHandle.TryOpenExisting(
                    RedirectContinueProbeName, out EventWaitHandle? resume))
                {
                    return;
                }
                using (resume)
                {
                    selected.Set();
                    resume.WaitOne(15_000);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The optional test gate must not affect normal activation.
        }
    }

    private static async Task PauseQueuedActivationForTestAsync()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                ActivationQueuedProbeName, out EventWaitHandle? queued))
            {
                return;
            }
            using (queued)
            {
                if (!EventWaitHandle.TryOpenExisting(
                    ActivationContinueProbeName, out EventWaitHandle? resume))
                {
                    return;
                }
                using (resume)
                {
                    queued.Set();
                    await Task.Run(() => resume.WaitOne(15_000));
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The optional test gate must not affect normal activation.
        }
    }

    private static void PauseAfterAcceptanceForTest()
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                AcceptanceReceivedProbeName, out EventWaitHandle? received))
            {
                return;
            }
            using (received)
            {
                if (!EventWaitHandle.TryOpenExisting(
                    AcceptanceContinueProbeName, out EventWaitHandle? resume))
                {
                    return;
                }
                using (resume)
                {
                    received.Set();
                    resume.WaitOne(15_000);
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The optional test gate must not affect normal activation.
        }
    }

    private static bool IsClosing(AppInstance instance)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(
                ClosingSignalName((int)instance.ProcessId),
                out EventWaitHandle? signal))
            {
                return false;
            }
            using (signal)
            {
                return signal.WaitOne(0);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SignalActivationProbe()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(
                ActivationProbeName, out EventWaitHandle? probe))
            {
                using (probe)
                {
                    probe.Set();
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The optional test probe must not affect normal activation.
        }
    }

}
