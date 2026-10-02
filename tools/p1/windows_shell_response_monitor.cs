using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// Samples window-message dispatch while UI Automation scrolls the TXT view.
public sealed class WindowResponseMonitor {
    private const uint WmNull = 0;
    private const uint SmtoAbortIfHung = 2;
    private const long SlowMilliseconds = 500;
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr handle, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeoutMilliseconds, out IntPtr result);
    private readonly IntPtr handle;
    private Thread worker;
    private volatile bool running;
    private long maximumMilliseconds;
    private long samples;
    private long slowSamples;
    private long timeouts;
    public WindowResponseMonitor(IntPtr handle) { this.handle = handle; }
    public long MaximumMilliseconds { get { return Interlocked.Read(ref maximumMilliseconds); } }
    public long Samples { get { return Interlocked.Read(ref samples); } }
    public long SlowSamples { get { return Interlocked.Read(ref slowSamples); } }
    public long Timeouts { get { return Interlocked.Read(ref timeouts); } }
    public void Start() {
        running = true;
        worker = new Thread(Run);
        worker.IsBackground = true;
        worker.Start();
    }
    public void Stop() {
        running = false;
        if (worker != null && !worker.Join(3000)) {
            throw new TimeoutException("UI response monitor did not stop.");
        }
    }
    private void Run() {
        while (running) {
            Stopwatch timer = Stopwatch.StartNew();
            IntPtr result;
            IntPtr status = SendMessageTimeout(
                handle, WmNull, IntPtr.Zero, IntPtr.Zero, SmtoAbortIfHung, 1000, out result);
            timer.Stop();
            Interlocked.Increment(ref samples);
            if (status == IntPtr.Zero) Interlocked.Increment(ref timeouts);
            long duration = timer.ElapsedMilliseconds;
            if (duration > SlowMilliseconds) Interlocked.Increment(ref slowSamples);
            long previous = Interlocked.Read(ref maximumMilliseconds);
            while (duration > previous) {
                long actual = Interlocked.CompareExchange(
                    ref maximumMilliseconds, duration, previous);
                if (actual == previous) break;
                previous = actual;
            }
            Thread.Sleep(25);
        }
    }
}
