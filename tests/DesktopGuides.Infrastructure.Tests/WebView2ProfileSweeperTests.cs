using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class WebView2ProfileSweeperTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "desktop-guides-sweep-" + Guid.NewGuid().ToString("N"));

    private string Cache => Path.Combine(root, "Cache");
    private string Profiles => Path.Combine(Cache, "WebView2");

    public void Dispose()
    {
        if (!Directory.Exists(root)) return;
        // Remove test links first, so cleanup never follows one.
        foreach (string folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .Where(path => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                     .ToList())
        {
            Directory.Delete(folder);
        }
        Directory.Delete(root, recursive: true);
    }

    private string Profile(string? name = null)
    {
        string path = Path.Combine(Profiles, name ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "Default", "Cache"));
        File.WriteAllText(Path.Combine(path, "Default", "Cache", "data_0"), "cache");
        File.WriteAllText(Path.Combine(path, "Local State"), "{}");
        return path;
    }

    [Fact]
    public void RemovesProfileFoldersWithTheirFiles()
    {
        string first = Profile();
        string second = Profile();

        Assert.Equal(2, WebView2ProfileSweeper.Sweep(Cache));

        Assert.False(Directory.Exists(first));
        Assert.False(Directory.Exists(second));
        Assert.True(Directory.Exists(Profiles));
    }

    [Fact]
    public void KeepsEverythingThatIsNotAProfile()
    {
        string other = Profile("keep-me");
        string dashed = Profile(Guid.NewGuid().ToString("D"));
        string file = Path.Combine(Profiles, Guid.NewGuid().ToString("N"));
        File.WriteAllText(file, "not a folder");
        string diagnostics = Path.Combine(Cache, "diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diagnostics);

        Assert.Equal(0, WebView2ProfileSweeper.Sweep(Cache));

        Assert.True(Directory.Exists(other));
        Assert.True(Directory.Exists(dashed));
        Assert.True(File.Exists(file));
        Assert.True(Directory.Exists(diagnostics));
    }

    [Fact]
    public void MissingProfilesFolderIsANoOp()
    {
        Assert.Equal(0, WebView2ProfileSweeper.Sweep(Cache));
        Assert.False(Directory.Exists(Cache));
    }

    [Fact]
    public void RemovesReadOnlyFiles()
    {
        string profile = Profile();
        string locked = Path.Combine(profile, "Local State");
        File.SetAttributes(locked, FileAttributes.ReadOnly);

        Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));
        Assert.False(Directory.Exists(profile));
    }

    [Fact]
    public void LockedProfileIsSkippedAndTheRestRemoved()
    {
        if (!OperatingSystem.IsWindows()) return;
        string held = Profile();
        string free = Profile();
        using (new FileStream(Path.Combine(held, "Local State"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));
        }

        Assert.True(File.Exists(Path.Combine(held, "Local State")));
        Assert.False(Directory.Exists(free));
    }

    [Fact]
    public void LinkInsideAProfileIsRemovedWithoutEnteringIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = Path.Combine(root, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "user file");
        string profile = Profile();
        RemovalLibrary.CreateJunction(Path.Combine(profile, "Default", "link"), target);

        Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));

        Assert.False(Directory.Exists(profile));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void ProfileThatIsALinkIsRemovedWithoutEnteringIt()
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = Path.Combine(root, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "user file");
        Directory.CreateDirectory(Profiles);
        string link = Path.Combine(Profiles, Guid.NewGuid().ToString("N"));
        RemovalLibrary.CreateJunction(link, target);

        Assert.Equal(1, WebView2ProfileSweeper.Sweep(Cache));

        Assert.False(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "keep.txt")));
    }

    [Fact]
    public void ProfilesFolderThatIsALinkIsLeftAlone()
    {
        if (!OperatingSystem.IsWindows()) return;
        string target = Path.Combine(root, "outside");
        string inside = Path.Combine(target, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(inside, "keep.txt"), "user file");
        Directory.CreateDirectory(Cache);
        RemovalLibrary.CreateJunction(Profiles, target);

        Assert.Equal(0, WebView2ProfileSweeper.Sweep(Cache));

        Assert.True(File.Exists(Path.Combine(inside, "keep.txt")));
    }
}
