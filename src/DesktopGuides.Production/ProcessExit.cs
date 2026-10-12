using System.Runtime.InteropServices;

namespace DesktopGuides.Production;

// #93: Windows.Data.Pdf keeps a process-wide Direct3D device in a static that
// its DLL destroys during ExitProcess. Under WARP, the software renderer used
// without a GPU, releasing it posts work to the thread pool that process
// shutdown has already torn down, and the process crashes with
// STATUS_INVALID_PARAMETER in ntdll. After a normal close the app has saved
// and released everything it owns, so it ends its own process instead, and
// no DLL's shutdown code runs.
internal static class ProcessExit
{
    internal static void EndAfterNormalClose() => TerminateProcess(GetCurrentProcess(), 0);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(nint process, uint exitCode);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
