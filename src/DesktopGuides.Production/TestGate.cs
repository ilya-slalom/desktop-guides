namespace DesktopGuides.Production;

// Installed tests switch on optional behavior by creating a named event
// before they drive the window. Without the event the app behaves normally.
internal static class TestGate
{
    public static bool IsOpen(string name)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(name, out EventWaitHandle? gate)) return false;
            gate.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
