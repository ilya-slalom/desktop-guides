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

    internal static void RejectMultipleHardLinks(string path, bool beingDeletedPasses = false)
    {
        using SafeFileHandle handle = File.OpenHandle(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        RejectMultipleHardLinks(handle, path, beingDeletedPasses);
    }

    internal static void RejectMultipleHardLinks(SafeFileHandle handle, string path, bool beingDeletedPasses)
    {
        if (!GetFileInformationByHandleEx(
                handle, FileStandardInfoClass, out FileStandardInfo info,
                (uint)Marshal.SizeOf<FileStandardInfo>()))
        {
            throw new IOException("Managed database file link count could not be checked.",
                new Win32Exception(Marshal.GetLastPInvokeError()));
        }
        string name = Path.GetFileName(path);
        // #138: SQLite deletes -wal and -shm as the last connection closes,
        // which can land while this handle is open. With POSIX delete the
        // name is gone (no links); otherwise the delete is pending. Such a
        // sidecar can't be followed, so it passes; the database never may.
        if (info.NumberOfLinks == 0 || info.DeletePending != 0)
        {
            if (beingDeletedPasses) return;
            throw new InvalidDataException($"Managed database file {name} is being deleted.");
        }
        if (info.NumberOfLinks != 1)
        {
            throw new InvalidDataException($"Managed database file {name} has {info.NumberOfLinks} hard links.");
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file, int informationClass,
        out FileStandardInfo information, uint bufferSize);
}
