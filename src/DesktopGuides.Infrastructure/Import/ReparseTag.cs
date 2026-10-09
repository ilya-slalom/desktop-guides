using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopGuides.Infrastructure.Import;

/// <summary>
/// Reads and classifies a reparse point's tag. Cloud Files placeholders
/// (OneDrive and other sync clients) are reparse points that don't redirect
/// the path, so they are the only kind a source path may cross.
/// </summary>
internal static class ReparseTag
{
    private const uint Cloud = 0x9000001A;
    private const uint CloudMask = 0x0000F000;

    public static bool IsCloudPlaceholder(uint tag) => (tag & ~CloudMask) == Cloud;

    /// <summary>
    /// Reads the tag from the directory entry, so a placeholder isn't
    /// opened and its content isn't downloaded.
    /// </summary>
    public static uint Of(string path)
    {
        IntPtr find = FindFirstFileEx(path, 1, out FindData data, 0, IntPtr.Zero, 0);
        if (find == new IntPtr(-1))
        {
            throw new IOException("A source path's link type could not be read.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        FindClose(find);
        return (data.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ? data.Reserved0 : 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes;
        // Three FILETIMEs: DWORD pairs, so no 8-byte alignment padding.
        public uint CreationLow, CreationHigh;
        public uint AccessLow, AccessHigh;
        public uint WriteLow, WriteHigh;
        public uint SizeHigh;
        public uint SizeLow;
        public uint Reserved0;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }

    // Info level 1 is FindExInfoBasic; search op 0 is FindExSearchNameMatch.
    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileExW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileEx(
        string fileName, int infoLevel, out FindData data,
        int searchOp, IntPtr searchFilter, uint flags);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr find);
}
