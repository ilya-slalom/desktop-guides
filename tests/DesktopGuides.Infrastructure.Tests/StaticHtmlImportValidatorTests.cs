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

    [Theory]
    [InlineData("100% Completion Guide.html", "guide.html")]
    [InlineData("100% Completion Guide.htm", "guide.htm")]
    public async Task PercentInSelectedEntryUsesSafeManagedNameAndRevalidates(
        string sourceName,
        string managedName)
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource(sourceName, """
            <img src="images/map.png">
            <img src="images/%2e%2e/outside.png">
            """);
        files.WriteSource("images/map.png", "image");
        StaticHtmlImportValidator validator = new();

        StaticHtmlImportPreview preview = await validator.PreviewAsync(
            Path.Combine(files.SourceRoot, sourceName));

        Assert.Equal(managedName, preview.EntryRelativePath);
        Assert.Equal(
            [managedName, "images/map.png"],
            preview.Manifest.Assets.Select(asset => asset.RelativePath));
        Assert.Equal(
            StaticReferenceStatus.Unsafe,
            Assert.Single(preview.Warnings).Status);
        Assert.False(File.Exists(Path.Combine(files.SourceRoot, managedName)));

        await files.StageAsync(preview);
        await validator.VerifyStagedAsync(preview, files.StageRoot);
        Assert.True(File.Exists(Path.Combine(files.StageRoot, managedName)));

        files.WriteSource(sourceName, "changed");
        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                validator.VerifyStagedAsync(preview, files.StageRoot));
        Assert.Equal(StaticHtmlValidationIssue.SourceChanged, error.Issue);
    }

    [Fact]
    public async Task PercentNamedSelectedEntrySymlinkFailsClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        string outside = Path.Combine(files.Root, "outside.html");
        File.WriteAllText(outside, "private");
        string selected = Path.Combine(
            files.SourceRoot, "100% Completion Guide.html");
        File.CreateSymbolicLink(selected, outside);

        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                new StaticHtmlImportValidator().PreviewAsync(selected));

        Assert.Equal(StaticHtmlValidationIssue.UnsafePath, error.Issue);
    }

    [Theory]
    [InlineData("100% Completion Guide_files")]
    [InlineData("100%25 Completion Guide_files")]
    [InlineData("100%25%20Completion%20Guide_files")]
    public async Task PercentNamedCompanionAssetsAreIncludedAndRevalidated(
        string referencedFolder)
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        const string sourceEntry = "100% Completion Guide.html";
        const string companion = "100% Completion Guide_files";
        string managedFolder = RootedStaticHtmlAssetSource.ManagedCompanionFolder;
        files.WriteSource(sourceEntry, $"""
            <link rel="stylesheet" href="{referencedFolder}/style.css">
            <img src="{referencedFolder}/map.png">
            <img src="{referencedFolder}/missing.png">
            <img src="{referencedFolder}/%2e%2e/outside.png">
            <img src="{managedFolder}/unrelated.png">
            """);
        files.WriteSource(companion + "/style.css",
            "body { background: url(map.png) }");
        files.WriteSource(companion + "/map.png", "image");
        files.WriteSource(managedFolder + "/unrelated.png", "unrelated");
        StaticHtmlImportValidator validator = new();

        StaticHtmlImportPreview preview = await validator.PreviewAsync(
            Path.Combine(files.SourceRoot, sourceEntry));

        Assert.Equal("guide.html", preview.EntryRelativePath);
        Assert.Equal(
            [managedFolder + "/map.png", managedFolder + "/style.css",
                "guide.html"],
            preview.Manifest.Assets.Select(asset => asset.RelativePath));
        Assert.Equal(
            companion + "/map.png",
            Assert.Single(preview.Manifest.Assets,
                asset => asset.Kind == StaticAssetKind.Image)
                .RequestRelativePath);
        Assert.Equal(
            companion + "/style.css",
            Assert.Single(preview.Manifest.Assets,
                asset => asset.Kind == StaticAssetKind.StyleSheet)
                .RequestRelativePath);
        Assert.Equal(
            [StaticReferenceStatus.Missing, StaticReferenceStatus.Unsafe,
                StaticReferenceStatus.Unsafe],
            preview.Warnings.Select(warning => warning.Status));
        Assert.Contains(preview.Manifest.References, reference =>
            reference.RawTarget == referencedFolder + "/map.png" &&
            reference.RelativePath == managedFolder + "/map.png" &&
            reference.Status == StaticReferenceStatus.Included);

        await files.StageAsync(preview);
        await validator.VerifyStagedAsync(preview, files.StageRoot);
        Assert.Equal(
            "image",
            files.ReadStage(managedFolder + "/map.png"));

        files.WriteSource(companion + "/map.png", "changed");
        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                validator.VerifyStagedAsync(preview, files.StageRoot));
        Assert.Equal(StaticHtmlValidationIssue.SourceChanged, error.Issue);
    }

    [Fact]
    public async Task PercentNamedCompanionLinkFailsClosed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        const string companion = "100% Completion Guide_files";
        files.WriteSource(
            "100% Completion Guide.html",
            $"""<img src="{companion}/linked.png">""");
        string outside = Path.Combine(files.Root, "outside.png");
        File.WriteAllText(outside, "private");
        string link = Path.Combine(
            files.SourceRoot, companion, "linked.png");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        File.CreateSymbolicLink(link, outside);

        StaticHtmlValidationException error = await Assert.ThrowsAsync<
            StaticHtmlValidationException>(() =>
                new StaticHtmlImportValidator().PreviewAsync(
                    Path.Combine(files.SourceRoot,
                        "100% Completion Guide.html")));

        Assert.Equal(StaticHtmlValidationIssue.UnsafePath, error.Issue);
    }

    [Fact]
    public async Task EncodedTripletInLiteralCompanionNameStaysUnsafe()
    {
        if (!OperatingSystem.IsWindows()) return;
        using TestHtmlDirectory files = new();
        files.WriteSource(
            "Guide%2e.html",
            """<img src="Guide%2e_files/map.png">""");
        files.WriteSource("Guide%2e_files/map.png", "image");

        StaticHtmlImportPreview preview = await new StaticHtmlImportValidator()
            .PreviewAsync(Path.Combine(files.SourceRoot, "Guide%2e.html"));

        Assert.Equal(
            StaticReferenceStatus.Unsafe,
            Assert.Single(preview.Warnings).Status);
        Assert.DoesNotContain(preview.Manifest.Assets,
            asset => asset.Kind == StaticAssetKind.Image);
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
        await files.StageAsync(preview);

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
        await files.StageAsync(preview);
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

        public string ReadStage(string relativePath) =>
            File.ReadAllText(GetPath(StageRoot, relativePath));

        public async Task StageAsync(StaticHtmlImportPreview preview)
        {
            RootedStaticHtmlAssetSource source = preview.CreateSource();
            foreach (StaticAsset asset in preview.Manifest.Assets)
            {
                string target = GetPath(StageRoot, asset.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using Stream input = await source.OpenReadAsync(
                    asset.RelativePath) ??
                    throw new FileNotFoundException(
                        "Preview asset vanished before staging.");
                await using FileStream output = File.Create(target);
                await input.CopyToAsync(output);
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
