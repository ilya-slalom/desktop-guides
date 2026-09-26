using System.Runtime.InteropServices;
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
    private static AppInstance? primaryInstance;
    private static EventWaitHandle? closingSignal;
    private static EventWaitHandle? activationAcceptedSignal;
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
        AppInstance? main = FindPrimaryOrRedirect(activation);
        if (main is null)
        {
            return;
        }

        primaryInstance = main;
        closingSignal = new EventWaitHandle(
            false, EventResetMode.ManualReset, ClosingSignalName(Environment.ProcessId));
        closingSignal.Reset();
        activationAcceptedSignal = new EventWaitHandle(
            false, EventResetMode.AutoReset,
            ActivationAcceptedName(Environment.ProcessId));
        activationAcceptedSignal.Reset();
        primaryInstance.Activated += (_, _) =>
        {
            Volatile.Read(ref uiQueue)?.TryEnqueue(
                async () =>
                {
                    await PauseQueuedActivationForTestAsync();
                    if (app?.ActivateMainWindow() == true)
                    {
                        activationAcceptedSignal?.Set();
                        SignalActivationProbe();
                    }
                });
        };

        try
        {
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
            activationAcceptedSignal.Dispose();
            activationAcceptedSignal = null;
            closingSignal.Dispose();
            closingSignal = null;
        }
    }

    internal static void ReleaseInstanceKey()
    {
        closingSignal?.Set();
        primaryInstance?.UnregisterKey();
        primaryInstance = null;
    }

    private static AppInstance? FindPrimaryOrRedirect(
        AppActivationArguments activation)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            AppInstance target = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (target.IsCurrent)
            {
                return target;
            }
            PauseAfterSelectingTargetForTest();
            using Mutex redirectGate = new(
                false, RedirectGateName((int)target.ProcessId));
            bool gateHeld;
            try
            {
                gateHeld = redirectGate.WaitOne(1000);
            }
            catch (AbandonedMutexException)
            {
                gateHeld = true;
            }
            if (!gateHeld)
            {
                continue;
            }
            try
            {
                AppInstance owner = AppInstance.FindOrRegisterForKey(InstanceKey);
                if (owner.IsCurrent)
                {
                    return owner;
                }
                if (owner.ProcessId != target.ProcessId || IsClosing(target))
                {
                    continue;
                }
                if (!EventWaitHandle.TryOpenExisting(
                    ActivationAcceptedName((int)target.ProcessId),
                    out EventWaitHandle? accepted))
                {
                    Thread.Sleep(50);
                    continue;
                }
                using (accepted)
                {
                    accepted.Reset();
                    try
                    {
                        RedirectActivation(activation, target);
                        lastError = null;
                    }
                    catch (Exception error)
                    {
                        lastError = error;
                    }
                    if (lastError is null &&
                        WaitForActivationAcceptance(accepted, target, deadline))
                    {
                        return null;
                    }
                }
            }
            finally
            {
                redirectGate.ReleaseMutex();
            }
            Thread.Sleep(50);
        }
        throw new TimeoutException(
            "Could not redirect or take over application activation.", lastError);
    }

    private static string ClosingSignalName(int processId) =>
        $@"Local\DesktopGuides.Preview.Closing.{processId}";

    private static string ActivationAcceptedName(int processId) =>
        $@"Local\DesktopGuides.Preview.ActivationAccepted.{processId}";

    private static string RedirectGateName(int processId) =>
        $@"Local\DesktopGuides.Preview.RedirectGate.{processId}";

    private static bool WaitForActivationAcceptance(
        EventWaitHandle accepted, AppInstance target, DateTime deadline)
    {
        DateTime acceptanceDeadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline &&
            DateTime.UtcNow < acceptanceDeadline)
        {
            if (IsClosing(target))
            {
                return false;
            }
            if (accepted.WaitOne(50))
            {
                return !IsClosing(target);
            }
            AppInstance owner = AppInstance.FindOrRegisterForKey(InstanceKey);
            if (owner.IsCurrent || owner.ProcessId != target.ProcessId)
            {
                return false;
            }
        }
        return false;
    }

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
