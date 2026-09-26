using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DesktopGuides.Infrastructure.Storage;

internal static class WindowsFileLinks
{
    // FILE_INFO_BY_HANDLE_CLASS.FileStandardInfo
    private const int FileStandardInfoClass = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending;
        public byte Directory;
    }

    internal static void RejectMultipleHardLinks(string path)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandleEx(
                handle, FileStandardInfoClass, out FileStandardInfo info,
                (uint)Marshal.SizeOf<FileStandardInfo>()))
        {
            throw new IOException("Managed database file link count could not be checked.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        if (info.NumberOfLinks != 1)
        {
            throw new InvalidDataException("Managed database file has multiple hard links.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int informationClass,
        out FileStandardInfo information, uint bufferSize);
}
