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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, string lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

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

    // The common Open dialog's Win32 controls reach managed UIA only as
    // panes without patterns, so the smoke drives them by window handle.
    public static void SetText(IntPtr window, string text)
    {
        if (SendMessage(window, 0x000C, IntPtr.Zero, text) == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not set the dialog text.");
        }
    }

    // Posts WM_COMMAND with BN_CLICKED for a dialog button id such as IDOK
    // or IDCANCEL. Posting keeps the caller from waiting on the dialog.
    public static void Command(IntPtr dialog, int buttonId, IntPtr button)
    {
        if (!PostMessage(dialog, 0x0111, new IntPtr(buttonId), button))
        {
            throw new InvalidOperationException("Could not send the dialog command.");
        }
    }
}
