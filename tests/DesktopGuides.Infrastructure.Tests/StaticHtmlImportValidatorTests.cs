using System.Diagnostics;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class StaticHtmlImportValidatorTests
{
    [Fact]
    public async Task PreviewReturnsBlockedAndMissingWarnings()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource("guide.html", """
            <img src="images/map.png">
            <img src="images/missing.png">
            <img src="../outside.png">
            <img src="images/%2e%2e/outside.png">
            <img src="https://example.test/remote.png">
            <img src="images/vector.svg">
            """);
        files.WriteSource("images/map.png", "image");
        File.WriteAllText(Path.Combine(files.Root, "outside.png"), "private");

        StaticHtmlImportPreview preview = await new StaticHtmlImportValidator()
            .PreviewAsync(files.EntryPath);

        Assert.Equal(
            ["guide.html", "images/map.png"],
            preview.Manifest.Assets.Select(asset => asset.RelativePath));
        Assert.Equal(
            [StaticReferenceStatus.Missing, StaticReferenceStatus.Unsafe,
                StaticReferenceStatus.Unsafe, StaticReferenceStatus.Remote,
                StaticReferenceStatus.Unsupported],
            preview.Warnings.Select(warning => warning.Status));
        Assert.All(preview.Warnings, warning =>
            Assert.False(string.IsNullOrWhiteSpace(warning.Message)));
    }

    [Fact]
    public async Task CaseCollidingAssetNamesFailPreview()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource("guide.html",
            """<img src="map.png"><img src="MAP.png">""");
        files.WriteSource("map.png", "image");

        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                new StaticHtmlImportValidator().PreviewAsync(files.EntryPath));

        Assert.Equal(StaticHtmlValidationIssue.CaseCollision, error.Issue);
    }

    [Fact]
    public async Task FileSymlinkCannotReadOutsideSelectedRoot()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource("guide.html", """<img src="linked.png">""");
        string outside = Path.Combine(files.Root, "outside.png");
        File.WriteAllText(outside, "private");
        string link = Path.Combine(files.SourceRoot, "linked.png");
        File.CreateSymbolicLink(link, outside);

        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                new StaticHtmlImportValidator().PreviewAsync(files.EntryPath));

        Assert.Equal(StaticHtmlValidationIssue.UnsafePath, error.Issue);
        Assert.Equal("private", File.ReadAllText(outside));
    }

    [Fact]
    public async Task JunctionCannotReadOutsideSelectedRoot()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource("guide.html", """<img src="linked/private.png">""");
        string outside = Path.Combine(files.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "private.png"), "private");
        string junction = Path.Combine(files.SourceRoot, "linked");
        CreateJunction(junction, outside);
        try
        {
            StaticHtmlValidationException error = await Assert.ThrowsAsync<
                StaticHtmlValidationException>(() =>
                    new StaticHtmlImportValidator().PreviewAsync(files.EntryPath));
            Assert.Equal(StaticHtmlValidationIssue.UnsafePath, error.Issue);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task SelectedRootCannotBeAJunction()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        string outside = Path.Combine(files.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "guide.html"), "private");
        string junction = Path.Combine(files.Root, "linked-root");
        CreateJunction(junction, outside);
        try
        {
            StaticHtmlValidationException error = await Assert.ThrowsAsync<
                StaticHtmlValidationException>(() =>
                    new StaticHtmlImportValidator().PreviewAsync(
                        Path.Combine(junction, "guide.html")));
            Assert.Equal(StaticHtmlValidationIssue.UnsafePath, error.Issue);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public async Task PostCopyCheckDetectsSourceAndStageChanges()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource("guide.html", """<img src="map.png">""");
        files.WriteSource("map.png", "first");
        StaticHtmlImportValidator validator = new();
        StaticHtmlImportPreview preview = await validator.PreviewAsync(
            files.EntryPath);
        files.Stage(preview);

        await validator.VerifyStagedAsync(preview, files.StageRoot);

        files.WriteSource("map.png", "later");
        StaticHtmlValidationException sourceError = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                validator.VerifyStagedAsync(preview, files.StageRoot));
        Assert.Equal(StaticHtmlValidationIssue.SourceChanged, sourceError.Issue);

        files.WriteSource("map.png", "first");
        files.WriteStage("map.png", "longer than before");
        StaticHtmlValidationException stageError = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                validator.VerifyStagedAsync(preview, files.StageRoot));
        Assert.Equal(StaticHtmlValidationIssue.StageChanged, stageError.Issue);
    }

    [Fact]
    public async Task LinkedStageFileFailsPostCopyCheck()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource("guide.html", """<img src="map.png">""");
        files.WriteSource("map.png", "image");
        StaticHtmlImportValidator validator = new();
        StaticHtmlImportPreview preview = await validator.PreviewAsync(
            files.EntryPath);
        files.Stage(preview);
        File.Delete(Path.Combine(files.StageRoot, "map.png"));
        File.CreateSymbolicLink(
            Path.Combine(files.StageRoot, "map.png"),
            Path.Combine(files.SourceRoot, "map.png"));

        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                validator.VerifyStagedAsync(preview, files.StageRoot));

        Assert.Equal(StaticHtmlValidationIssue.UnsafePath, error.Issue);
    }

    private static void CreateJunction(string junction, string target)
    {
        using Process process = Process.Start(new ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{junction}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private sealed class TestHtmlDirectory : IDisposable
    {
        public TestHtmlDirectory()
        {
            Root = Path.Combine(Path.GetTempPath(),
                "desktop-guides-html-" + Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            StageRoot = Path.Combine(Root, "stage");
            Directory.CreateDirectory(SourceRoot);
            Directory.CreateDirectory(StageRoot);
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string StageRoot { get; }
        public string EntryPath => Path.Combine(SourceRoot, "guide.html");

        public void WriteSource(string relativePath, string text) =>
            Write(SourceRoot, relativePath, text);

        public void WriteStage(string relativePath, string text) =>
            Write(StageRoot, relativePath, text);

        public void Stage(StaticHtmlImportPreview preview)
        {
            foreach (StaticAsset asset in preview.Manifest.Assets)
            {
                string source = GetPath(SourceRoot, asset.RelativePath);
                string target = GetPath(StageRoot, asset.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static void Write(string root, string relativePath, string text)
        {
            string path = GetPath(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        private static string GetPath(string root, string relativePath) =>
            Path.Combine(root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
