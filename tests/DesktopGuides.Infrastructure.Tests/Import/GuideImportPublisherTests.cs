using System.Security.Cryptography;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideImportPublisherTests
{
    [Fact]
    public async Task PublishesUtf8TextGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        IReadOnlyList<FileFingerprint> original = FileFingerprint.Of(source);
        ImportManifest manifest = await harness.InspectAsync(source);

        Guid id = await harness.PublishAsync(harness.Publisher(), manifest);

        Assert.Equal(harness.GuideId, id);
        Guide guide = Assert.Single(await harness.Repository.ListGuidesAsync(harness.Game.Id));
        byte[] bytes = File.ReadAllBytes(source);
        Assert.Equal(
            (id, "Imported Guide", GuideFormat.Txt, $"content/{id:N}", "guide.txt",
                Convert.ToHexStringLower(SHA256.HashData(bytes)), (long)bytes.Length, "notes.txt", (int?)null),
            (guide.Id, guide.Title, guide.Format, guide.ManagedRelativeRoot, guide.PrimaryRelativePath,
                guide.ContentSha256, guide.ContentBytes, guide.SourceLabel, guide.TextCodePage));
        Assert.Equal(bytes, File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, "guide.txt")));
        Assert.Null((await harness.Repository.GetReadingStateAsync(id))!.LocatorJson);
        Assert.NotNull(await harness.Repository.GetReaderPreferencesAsync(id));
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Equal(original, FileFingerprint.Of(source));
    }

    [Fact]
    public async Task PublishesLegacyTextWithTheChosenCodePage()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-legacy.txt", "legacy.txt");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source, 437));

        Assert.Equal(437, (await harness.Repository.GetGuideAsync(id))!.TextCodePage);
    }

    [Fact]
    public async Task PublishesPdfGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("pdf-short.pdf", "short.pdf");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal((GuideFormat.Pdf, "guide.pdf", 896L), (guide.Format, guide.PrimaryRelativePath, guide.ContentBytes));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, "guide.pdf")));
    }

    [Fact]
    public async Task GuideSurvivesDeletingTheOriginal()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(source));

        File.Delete(source);

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        byte[] managed = File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, guide.PrimaryRelativePath));
        Assert.Equal(guide.ContentSha256, Convert.ToHexStringLower(SHA256.HashData(managed)));
    }

    [Fact]
    public async Task SourceRemovedBeforeCopyIsMissing()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        File.Delete(source);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal((ImportIssue.Missing, "The file is no longer there. Choose it again."), (error.Issue, error.Message));
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task SourceGrownAfterPreviewIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        File.AppendAllText(source, "more");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        Assert.Equal(
            "The file changed after it was checked. Choose it again to see the new version.", error.Message);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task SourceRewrittenDuringCopyIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point != ImportCheckpoint.Copied) return;
            byte[] bytes = File.ReadAllBytes(source);
            bytes[0] ^= 0x20;
            File.WriteAllBytes(source, bytes);
            File.SetLastWriteTimeUtc(source, manifest.Source.LastWriteUtc.UtcDateTime.AddSeconds(2));
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task LockedSourceIsUnreadable()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        using FileStream locked = new(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        harness.AssertNothingLeft();
    }

    [Theory]
    [InlineData(nameof(ImportCheckpoint.Prepared))]
    [InlineData(nameof(ImportCheckpoint.Copied))]
    [InlineData(nameof(ImportCheckpoint.Verified))]
    [InlineData(nameof(ImportCheckpoint.Renamed))]
    public async Task CancellationBeforeCommitRollsBack(string checkpoint)
    {
        ImportCheckpoint at = Enum.Parse<ImportCheckpoint>(checkpoint);
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        IReadOnlyList<FileFingerprint> original = FileFingerprint.Of(source);
        ImportManifest manifest = await harness.InspectAsync(source);
        using CancellationTokenSource cancel = new();
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == at) cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.PublishAsync(publisher, manifest, token: cancel.Token));

        harness.AssertNothingLeft();
        Assert.Equal(original, FileFingerprint.Of(source));
    }

    [Fact]
    public async Task CancellationFromProgressRollsBack()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        using CancellationTokenSource cancel = new();
        SyncProgress progress = new(value =>
        {
            if (!value.Publishing) cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.PublishAsync(harness.Publisher(), manifest, progress, cancel.Token));

        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task CancellationInsideTheCommitIsIgnored()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        using CancellationTokenSource cancel = new();
        List<ImportProgress> reports = [];
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.InCommit) cancel.Cancel();
        });

        Guid id = await harness.PublishAsync(publisher, manifest, new SyncProgress(reports.Add), cancel.Token);

        Assert.NotNull(await harness.Repository.GetGuideAsync(id));
        Assert.Equal(new ImportProgress(1, true), reports[^1]);
        Assert.All(reports.SkipLast(1), report => Assert.True(report is { Publishing: false, Fraction: < 1 }));
    }

    [Fact]
    public async Task FullDiskIsNotEnoughSpace()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(createStagedFile: _ => new DiskFullStream()), manifest));

        Assert.Equal(
            (ImportIssue.NotEnoughSpace, "There isn't enough free space to import this guide."),
            (error.Issue, error.Message));
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task ExistingContentDirectoryIsSaveFailedAndKept()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher();
        string foreign = Path.Combine(harness.Paths.GetGuideRoot(harness.GuideId), "foreign.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        File.WriteAllText(foreign, "not ours");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(
            (ImportIssue.SaveFailed, "The guide couldn't be saved to your library. Nothing was changed."),
            (error.Issue, error.Message));
        Assert.Equal("not ours", File.ReadAllText(foreign));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Equal(0, harness.Count("Guides") + harness.Count("FileOperations"));
    }

    [Fact]
    public async Task FailureInsideTheCommitIsSaveFailed()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.InCommit) throw new InvalidOperationException("injected");
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task GameDeletedBeforeCommitIsSaveFailed()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.Renamed) harness.Execute("DELETE FROM Games WHERE Id = $id", harness.Game.Id);
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.SaveFailed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Theory]
    [InlineData(nameof(ImportCheckpoint.Prepared))]
    [InlineData(nameof(ImportCheckpoint.Copied))]
    [InlineData(nameof(ImportCheckpoint.Verified))]
    [InlineData(nameof(ImportCheckpoint.Renamed))]
    [InlineData(nameof(ImportCheckpoint.InCommit))]
    public async Task CrashIsRolledBackAtStartup(string checkpoint)
    {
        ImportCheckpoint at = Enum.Parse<ImportCheckpoint>(checkpoint);
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher crashing = harness.Publisher(
            checkpoint: point =>
            {
                if (point == at) throw new InvalidOperationException("crash");
            },
            rollBack: (_, _) => { });
        await Assert.ThrowsAsync<GuideImportException>(() => harness.PublishAsync(crashing, manifest));
        Assert.Equal(1, harness.Count("FileOperations"));

        await harness.Repository.InitializeAsync();

        Assert.Equal(1, harness.Repository.LastStartupReconciliation!.ResolvedOperationCount);
        harness.AssertNothingLeft();
        Guid later = await harness.PublishAsync(harness.Publisher(), manifest);
        Assert.NotNull(await harness.Repository.GetGuideAsync(later));
    }

    [Fact]
    public async Task FailedRollBackStillReportsTheOriginalIssue()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        ImportManifest manifest = await harness.InspectAsync(harness.Sources.Copy("txt-utf8.txt", "notes.txt"));
        GuideImportPublisher publisher = harness.Publisher(
            createStagedFile: _ => new DiskFullStream(),
            rollBack: (_, _) => throw new IOException("rollback failed"));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.NotEnoughSpace, error.Issue);
        Assert.Equal(1, harness.Count("FileOperations"));
        await harness.Repository.InitializeAsync();
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task ImportHoldsTheWriteGate()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string source = harness.Sources.Copy("txt-utf8.txt", "notes.txt");
        ImportManifest manifest = await harness.InspectAsync(source);
        Guid earlier = await harness.PublishAsync(harness.Publisher(), manifest);
        using ManualResetEventSlim paused = new();
        using ManualResetEventSlim resume = new();
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point != ImportCheckpoint.Copied) return;
            paused.Set();
            resume.Wait();
        });
        Task<Guid> import = harness.PublishAsync(publisher, manifest);
        Assert.True(paused.Wait(TimeSpan.FromSeconds(10)));

        Task save = harness.Repository.SaveReadingLocationAsync(earlier, "{\"one\":1}", 0.25);
        await Task.Delay(200);
        Assert.False(save.IsCompleted);

        resume.Set();
        await import;
        await save;
        Assert.Equal("{\"one\":1}", (await harness.Repository.GetReadingStateAsync(earlier))!.LocatorJson);
    }

    private const string HtmlStaticFingerprint = "743c87a4c5222c99cb5de3f4ee931a63b994a54be3dc9ec5855c855dafcd3f21";

    [Fact]
    public async Task PublishesStaticHtmlGuide()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        IReadOnlyList<FileFingerprint> original = FileFingerprint.Of(harness.Sources.Root);

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal(
            (GuideFormat.Html, "guide.html", HtmlStaticFingerprint, 722L, "guide.html"),
            (guide.Format, guide.PrimaryRelativePath, guide.ContentSha256, guide.ContentBytes, guide.SourceLabel));
        foreach (string file in new[] { "guide.html", "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            Assert.Equal(
                File.ReadAllBytes(P0Fixtures.Resolve("html-static/" + file)),
                File.ReadAllBytes(harness.Paths.ResolveExistingGuideFile(id, file)));
        }
        Assert.Equal(0, harness.Count("FileOperations"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness.Paths.StagingRoot));
        Assert.Equal(original, FileFingerprint.Of(harness.Sources.Root));
    }

    [Fact]
    public async Task PercentNamedEntryPublishesAsGuideHtml()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "100% Walkthrough.html");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        Guide guide = (await harness.Repository.GetGuideAsync(id))!;
        Assert.Equal(
            ("guide.html", "100% Walkthrough.html", HtmlStaticFingerprint),
            (guide.PrimaryRelativePath, guide.SourceLabel, guide.ContentSha256));
        Assert.True(File.Exists(harness.Paths.ResolveExistingGuideFile(id, "guide.html")));
    }

    [Fact]
    public async Task OnlyScannedFilesAreCopied()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        harness.Sources.Write("notes.txt", "not referenced");

        Guid id = await harness.PublishAsync(harness.Publisher(), await harness.InspectAsync(entry));

        Assert.Equal(4, Directory.EnumerateFiles(harness.Paths.GetGuideRoot(id), "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task AssetEditedAfterPreviewIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        ImportManifest manifest = await harness.InspectAsync(entry);
        harness.Sources.Write("styles/main.css", "body { color: red; }");

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(harness.Publisher(), manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    [Fact]
    public async Task AssetEditedDuringCopyIsChanged()
    {
        await using PublisherHarness harness = await PublisherHarness.CreateAsync();
        string entry = CopyHtmlStatic(harness, "guide.html");
        ImportManifest manifest = await harness.InspectAsync(entry);
        GuideImportPublisher publisher = harness.Publisher(checkpoint: point =>
        {
            if (point == ImportCheckpoint.Copied) harness.Sources.Write("images/map.png", [1, 2, 3]);
        });

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => harness.PublishAsync(publisher, manifest));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        harness.AssertNothingLeft();
    }

    private static string CopyHtmlStatic(PublisherHarness harness, string entryName)
    {
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            harness.Sources.Copy("html-static/" + file, file);
        }
        return harness.Sources.Copy("html-static/guide.html", entryName);
    }

    private sealed class SyncProgress(Action<ImportProgress> report) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => report(value);
    }
}
