using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class ManagedPathResolverTests
{
    [Fact]
    public void ResolvesOnlyExistingFilesInsideGeneratedGuideRoot()
    {
        using TestLibrary directory = new();
        ManagedPathResolver paths = directory.Paths;
        Guid guideId = Guid.NewGuid();
        string expected = paths.GetPlannedGuideFile(guideId, "styles/main.css");
        Directory.CreateDirectory(Path.GetDirectoryName(expected)!);
        File.WriteAllText(expected, "body { color: black }");

        Assert.Equal(expected, paths.ResolveExistingGuideFile(guideId, "styles/main.css"));
        Assert.StartsWith(paths.ContentRoot, expected, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(guideId.ToString("N"), Path.GetFileName(paths.GetGuideRoot(guideId)));
        Assert.Throws<FileNotFoundException>(() =>
            paths.ResolveExistingGuideFile(guideId, "styles/missing.css"));
    }

    [Fact]
    public void RejectsFileAndDirectoryLinks()
    {
        using TestLibrary directory = new();
        ManagedPathResolver paths = directory.Paths;
        Guid guideId = Guid.NewGuid();
        string outside = Path.Combine(directory.Root, "outside.txt");
        File.WriteAllText(outside, "secret");
        string fileLink = paths.GetPlannedGuideFile(guideId, "linked.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(fileLink)!);
        File.CreateSymbolicLink(fileLink, outside);

        Assert.Throws<InvalidDataException>(() =>
            paths.ResolveExistingGuideFile(guideId, "linked.txt"));

        string outsideDirectory = Path.Combine(directory.Root, "outside");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "secret.txt"), "secret");
        string directoryLink = Path.Combine(paths.GetGuideRoot(guideId), "linked");
        Directory.CreateSymbolicLink(directoryLink, outsideDirectory);
        Assert.Throws<InvalidDataException>(() =>
            paths.ResolveExistingGuideFile(guideId, "linked/secret.txt"));
    }

    // SQLite deletes -wal as a connection closes. While that delete is pending,
    // Windows denies access to the file; the check must not fail the open.
    [Fact]
    public void ASidecarBeingDeletedPassesTheCheck()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestLibrary directory = new();
        ManagedPathResolver paths = directory.Paths;
        paths.EnsureCreated();
        File.WriteAllText(paths.DatabasePath, "");
        string wal = paths.DatabasePath + "-wal";
        File.WriteAllText(wal, "wal");
        using SafeFileHandle pending = DeletePending.Open(wal);

        paths.ValidateDatabasePath();
    }

    [Fact]
    public void RejectsWindowsJunctions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using TestLibrary directory = new();
        ManagedPathResolver paths = directory.Paths;
        Guid guideId = Guid.NewGuid();
        Directory.CreateDirectory(paths.GetGuideRoot(guideId));
        string outside = Path.Combine(directory.Root, "junction-target");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");
        string junction = Path.Combine(paths.GetGuideRoot(guideId), "junction");
        ProcessStartInfo start = new(
            "cmd.exe", $"/c mklink /J \"{junction}\" \"{outside}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using Process process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                paths.ResolveExistingGuideFile(guideId, "junction/secret.txt"));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void RejectsLinkedRecoveryRoot()
    {
        using TestLibrary directory = new();
        ManagedPathResolver paths = directory.Paths;
        string outside = Path.Combine(directory.Root, "outside-recovery");
        Directory.CreateDirectory(outside);
        Directory.Delete(paths.RecoveryRoot);
        Directory.CreateSymbolicLink(paths.RecoveryRoot, outside);
        try
        {
            Assert.Throws<InvalidDataException>(() => paths.EnsureCreated());
        }
        finally
        {
            Directory.Delete(paths.RecoveryRoot);
        }
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("styles\\main.css")]
    [InlineData("C:/outside.txt")]
    [InlineData("styles/%2fmain.css")]
    public void RejectsUnsafeStoredPaths(string path)
    {
        using TestLibrary directory = new();
        Assert.Throws<InvalidDataException>(() =>
            directory.Paths.GetPlannedGuideFile(Guid.NewGuid(), path));
    }

    [Fact]
    public void ArtworkRootsSitBesideContentAndOutsideGuideStaging()
    {
        string root = Path.Combine(Path.GetTempPath(), "desktop-guides-paths-" + Guid.NewGuid());
        try
        {
            ManagedPathResolver paths = new(root);
            paths.EnsureCreated();
            Assert.Equal(Path.Combine(paths.LibraryRoot, "artwork"), paths.ArtworkRoot);
            Assert.Equal(Path.Combine(paths.LibraryRoot, ".artwork-staging"), paths.ArtworkStagingRoot);
            Assert.True(Directory.Exists(paths.ArtworkRoot));
            Assert.True(Directory.Exists(paths.ArtworkStagingRoot));
            Assert.False(paths.ArtworkStagingRoot.StartsWith(paths.StagingRoot + Path.DirectorySeparatorChar));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class TestLibrary : IDisposable
    {
        public TestLibrary()
        {
            Root = Path.Combine(Path.GetTempPath(), "desktop-guides-paths-" + Guid.NewGuid());
            Paths = new ManagedPathResolver(Path.Combine(Root, "app-data"));
            Paths.EnsureCreated();
        }

        public string Root { get; }
        public ManagedPathResolver Paths { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }

    // A handle with DELETE access marked for the classic (non-POSIX) delete:
    // the name stays, and other opens are denied, until the handle closes.
    private static class DeletePending
    {
        private const uint Delete = 0x00010000;
        private const uint GenericRead = 0x80000000;
        private const uint ShareAll = 7;
        private const uint OpenExisting = 3;
        private const int FileDispositionInfo = 4;

        public static SafeFileHandle Open(string path)
        {
            SafeFileHandle handle = CreateFileW(
                path, Delete | GenericRead, ShareAll, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) throw new Win32Exception();
            byte deleteFile = 1;
            if (!SetFileInformationByHandle(handle, FileDispositionInfo, ref deleteFile, 1))
            {
                handle.Dispose();
                throw new Win32Exception();
            }
            return handle;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFileW(
            string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle handle, int infoClass, ref byte info, uint size);
    }
}
