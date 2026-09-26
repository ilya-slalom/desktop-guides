using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace DesktopGuides.Production;

internal static class ForegroundActivation
{
    private const int SwRestore = 9;

    public static void AllowFor(int processId)
    {
        // A launch started by the foreground user process can transfer its
        // foreground permission to the existing app before the pipe request.
        AllowSetForegroundWindow(processId);
    }

    public static bool Activate(Window window)
    {
        window.Activate();
        nint handle = WindowNative.GetWindowHandle(window);
        if (handle == 0)
        {
            return false;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, SwRestore);
        }
        if (GetForegroundWindow() != handle)
        {
            // Windows flashes the taskbar button if its foreground lock
            // denies this request.
            SetForegroundWindow(handle);
        }
        return true;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
