using System;
using System.Runtime.InteropServices;

public static class DesktopGuidesForegroundProbe
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(
        IntPtr window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    public static void Click(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            throw new InvalidOperationException("Could not position the foreground probe cursor.");
        }
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Resize(IntPtr window, int x, int y, int width, int height)
    {
        if (!MoveWindow(window, x, y, width, height, true))
        {
            throw new InvalidOperationException("Could not resize the shell window.");
        }
    }

    public static uint Dpi(IntPtr window)
    {
        uint dpi = GetDpiForWindow(window);
        if (dpi == 0)
        {
            throw new InvalidOperationException("Could not read the shell window DPI.");
        }
        return dpi;
    }
}
