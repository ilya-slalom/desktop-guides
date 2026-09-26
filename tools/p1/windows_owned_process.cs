using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

public sealed class DesktopGuidesOwnedProcess : IDisposable
{
    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x102;
    private const uint WaitFailed = 0xFFFFFFFF;
    private IntPtr handle;

    private DesktopGuidesOwnedProcess(IntPtr handle)
    {
        this.handle = handle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(
        IntPtr processHandle, out long created, out long exited,
        out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle, int flags, StringBuilder imagePath, ref int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr processHandle, uint milliseconds);

    public static DesktopGuidesOwnedProcess OpenVerified(
        int processId, DateTime startedAt, int sessionId, string executablePath)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException("processId");

        IntPtr opened = OpenProcess(
            ProcessTerminate | ProcessQueryLimitedInformation | Synchronize,
            false, (uint)processId);
        if (opened == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 87) // The helper's short-lived child has already exited.
                return null;
            throw new Win32Exception(error, "Could not open the reported process.");
        }

        try
        {
            uint actualId = GetProcessId(opened);
            if (actualId != (uint)processId)
                throw new InvalidOperationException("Reported process ID changed.");

            long created;
            long exited;
            long kernel;
            long user;
            if (!GetProcessTimes(opened, out created, out exited, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read process creation time.");
            if (created != startedAt.ToUniversalTime().ToFileTimeUtc())
                throw new InvalidOperationException("Reported process creation time changed.");

            if (IsExited(opened))
                return null;

            uint actualSession;
            if (!ProcessIdToSessionId(actualId, out actualSession))
            {
                if (IsExited(opened))
                    return null;
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read process session.");
            }
            if (actualSession != (uint)sessionId)
                throw new InvalidOperationException("Reported process session changed.");

            var imagePath = new StringBuilder(32768);
            int length = imagePath.Capacity;
            if (!QueryFullProcessImageName(opened, 0, imagePath, ref length))
            {
                if (IsExited(opened))
                    return null;
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read process image path.");
            }
            if (!string.Equals(imagePath.ToString(), executablePath,
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Reported process image path changed.");

            if (IsExited(opened))
                return null;

            var owned = new DesktopGuidesOwnedProcess(opened);
            opened = IntPtr.Zero;
            return owned;
        }
        finally
        {
            if (opened != IntPtr.Zero)
                CloseHandle(opened);
        }
    }

    public bool HasExited
    {
        get
        {
            if (handle == IntPtr.Zero)
                throw new ObjectDisposedException("DesktopGuidesOwnedProcess");
            return IsExited(handle);
        }
    }

    public bool TerminateAndWait(int timeoutMilliseconds)
    {
        if (handle == IntPtr.Zero)
            throw new ObjectDisposedException("DesktopGuidesOwnedProcess");
        return TerminateAndWait(handle, timeoutMilliseconds);
    }

    public static bool TerminateAndWait(IntPtr processHandle, int timeoutMilliseconds)
    {
        if (timeoutMilliseconds < 0)
            throw new ArgumentOutOfRangeException("timeoutMilliseconds");
        if (IsExited(processHandle))
            return true;
        if (!TerminateProcess(processHandle, 1) && !IsExited(processHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not terminate test-owned process.");
        return WaitForExit(processHandle, (uint)timeoutMilliseconds);
    }

    private static bool IsExited(IntPtr processHandle)
    {
        return WaitForExit(processHandle, 0);
    }

    private static bool WaitForExit(IntPtr processHandle, uint milliseconds)
    {
        uint result = WaitForSingleObject(processHandle, milliseconds);
        if (result == WaitObject0)
            return true;
        if (result == WaitTimeout)
            return false;
        if (result == WaitFailed)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not wait for test-owned process.");
        throw new InvalidOperationException("Unexpected process wait result.");
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            handle = IntPtr.Zero;
        }
    }
}
