using DesktopGuides.Infrastructure.Storage;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

// #138: SQLite deletes -wal and -shm as the last connection closes, which
// can land between the check opening a sidecar and reading its links. On
// current Windows the name goes at once, so the handle sees no links.
public sealed class WindowsFileLinksTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"file-links-{Guid.NewGuid():N}");

    public WindowsFileLinksTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private (SafeFileHandle Handle, string Path) OpenThenDelete(string name)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, [1]);
        SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        File.Delete(path);
        return (handle, path);
    }

    [Fact]
    public void ASidecarDeletedDuringTheCheckPasses()
    {
        if (!OperatingSystem.IsWindows()) return;
        (SafeFileHandle handle, string path) = OpenThenDelete("library.db-wal");
        using (handle)
        {
            WindowsFileLinks.RejectMultipleHardLinks(handle, path, beingDeletedPasses: true);
        }
    }

    [Fact]
    public void TheDatabaseDeletedDuringTheCheckIsRefusedByName()
    {
        if (!OperatingSystem.IsWindows()) return;
        (SafeFileHandle handle, string path) = OpenThenDelete("library.db");
        using (handle)
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => WindowsFileLinks.RejectMultipleHardLinks(handle, path, beingDeletedPasses: false));
            Assert.Equal("Managed database file library.db is being deleted.", error.Message);
        }
    }
}
