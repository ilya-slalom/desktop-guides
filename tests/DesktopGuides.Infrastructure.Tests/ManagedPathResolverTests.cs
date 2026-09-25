using DesktopGuides.Infrastructure.Storage;
using System.Diagnostics;
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
}
