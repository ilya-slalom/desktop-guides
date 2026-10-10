using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void mouse_event(
        uint flags, uint x, uint y, uint data, UIntPtr extraInfo);

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);

    // What Settings sends after it changes AppsUseLightTheme, so running
    // apps re-read the app theme. HWND_BROADCAST, WM_SETTINGCHANGE,
    // SMTO_ABORTIFHUNG.
    public static bool BroadcastThemeChange()
    {
        IntPtr result;
        return SendMessageTimeout(
            new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet",
            0x0002, 5000, out result) != IntPtr.Zero;
    }

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

    // WebView2 content sits in a top-level Chromium window owned by its
    // browser process, outside the shell window's UIA tree. Returns every
    // window, top-level or child, of a class owned by one of the processes.
    public static IntPtr[] FindWindows(string className, uint[] processIds)
    {
        List<IntPtr> found = new List<IntPtr>();
        HashSet<uint> owners = new HashSet<uint>(processIds);
        EnumWindowsProc check = (window, data) =>
        {
            StringBuilder name = new StringBuilder(256);
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owners.Contains(owner) && GetClassName(window, name, name.Capacity) > 0 &&
                name.ToString() == className)
            {
                found.Add(window);
            }
            return true;
        };
        EnumWindows((window, data) =>
        {
            check(window, data);
            EnumChildWindows(window, check, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return found.ToArray();
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

    // Types into an edit box with WM_CHAR, as a user would. The Save
    // dialog tracks its file name from typing and ignores WM_SETTEXT.
    public static void TypeText(IntPtr window, string text)
    {
        SetText(window, string.Empty);
        foreach (char character in text)
        {
            SendMessage(window, 0x0102, new IntPtr(character), IntPtr.Zero);
        }
    }

    public static string GetText(IntPtr window)
    {
        StringBuilder text = new StringBuilder(1024);
        SendMessage(window, 0x000D, new IntPtr(text.Capacity), text);
        return text.ToString();
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
