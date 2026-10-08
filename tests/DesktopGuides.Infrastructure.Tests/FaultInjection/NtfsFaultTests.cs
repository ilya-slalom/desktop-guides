using System.Diagnostics;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

public sealed class NtfsFaultTests : IAsyncLifetime
{
    private FaultFixture fixture = null!;
    private string outside = null!;
    private string sentinel = null!;

    public async Task InitializeAsync()
    {
        fixture = await FaultFixture.CreateAsync();
        outside = Path.Combine(fixture.Library.Root, "outside");
        sentinel = Path.Combine(outside, "sentinel.txt");
        RemovalLibrary.WriteFile(outside, "sentinel.txt", "keep me");
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private void AssertSentinelIntact() => Assert.Equal("keep me", File.ReadAllText(sentinel));

    private static void HardLink(string link, string target)
    {
        using Process mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    [Fact]
    public async Task AJunctionedStagingOperationIsRefusedAtStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid operation = Guid.NewGuid();
        await fixture.Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, Guid.NewGuid());
            return Task.FromResult(true);
        }, CancellationToken.None);
        string link = Path.Combine(fixture.Paths.StagingRoot, operation.ToString("N"));
        RemovalLibrary.CreateJunction(link, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RestartAsync());

            AssertSentinelIntact();
            Assert.Equal("Import|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task AJunctionedTrashOperationIsRefusedAtStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        Guid operation = Guid.NewGuid();
        await fixture.Library.RunDeletion(journal => journal.Prepare(operation, fixture.SubjectGuide));
        string link = Path.Combine(fixture.Paths.TrashRoot, operation.ToString("N"));
        RemovalLibrary.CreateJunction(link, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RestartAsync());

            AssertSentinelIntact();
            Assert.Equal("DeleteGuide|Prepared", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task AJunctionedCommittedTrashGuideIsRefusedAtStartup()
    {
        if (!OperatingSystem.IsWindows()) return;
        await fixture.GuideRemover(finish: FaultFixture.SkipFinish).RemoveAsync(fixture.SubjectGuide);
        string operation = fixture.Library.Scalar("SELECT Id FROM FileOperations");
        string trashed = Path.Combine(fixture.Paths.TrashRoot, operation, fixture.SubjectGuide.ToString("N"));
        Directory.Move(trashed, Path.Combine(outside, "moved"));
        RemovalLibrary.CreateJunction(trashed, outside);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.RestartAsync());

            AssertSentinelIntact();
            Assert.Equal("DeleteGuide|Committed", fixture.Library.Scalar("SELECT Kind || '|' || Phase FROM FileOperations"));
        }
        finally
        {
            Directory.Delete(trashed);
        }
    }

    [Fact]
    public async Task AJunctionedGuideFolderIsUnsafeToRemove()
    {
        if (!OperatingSystem.IsWindows()) return;
        string content = fixture.Paths.GetGuideRoot(fixture.SubjectGuide);
        Directory.Move(content, Path.Combine(outside, "guide"));
        RemovalLibrary.CreateJunction(content, Path.Combine(outside, "guide"));
        try
        {
            LibrarySnapshot start = fixture.Capture();
            GuideRemover guides = fixture.GuideRemover();
            GameRemover games = fixture.GameRemover();

            Assert.Equal(GuideRemovalIssue.Unsafe, (await Assert.ThrowsAsync<GuideRemovalException>(
                () => guides.DescribeAsync(fixture.SubjectGuide))).Issue);
            Assert.Equal(GuideRemovalIssue.Unsafe, (await Assert.ThrowsAsync<GuideRemovalException>(
                () => guides.RemoveAsync(fixture.SubjectGuide))).Issue);
            Assert.Equal(GameRemovalIssue.Unsafe, (await Assert.ThrowsAsync<GameRemovalException>(
                () => games.RemoveAsync(fixture.SubjectGame, 2))).Issue);

            SnapshotAssert.Unchanged(start, fixture.Capture());
            AssertSentinelIntact();
        }
        finally
        {
            Directory.Delete(content);
        }
    }

    [Fact]
    public async Task AHardLinkedGuideFileKeepsTheOutsideFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(fixture.Paths.GetGuideRoot(fixture.SubjectGuide), "guide.txt");
        File.Delete(file);
        HardLink(file, sentinel);

        GuideRemovalResult result = await fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide);

        Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
        AssertSentinelIntact();
    }

    [Fact]
    public async Task ReservedNamesInARemovedGuideReachAConsistentOutcome()
    {
        if (!OperatingSystem.IsWindows()) return;
        string device = @"\\?\" + fixture.Paths.GetGuideRoot(fixture.SubjectGuide);
        File.WriteAllText(Path.Combine(device, "CON"), "reserved");
        File.WriteAllText(Path.Combine(device, "x."), "trailing dot");
        try
        {
            GuideRemovalResult result = await fixture.GuideRemover().RemoveAsync(fixture.SubjectGuide);
            await fixture.RestartAsync();

            Assert.Equal(GuideRemovalOutcome.Removed, result.Outcome);
            Assert.Equal("0", fixture.Library.Scalar($"SELECT COUNT(*) FROM Guides WHERE Id = '{fixture.SubjectGuide:N}'"));
            string operations = fixture.Library.Scalar("SELECT COUNT(*) || '|' || IFNULL(MAX(Phase), '') FROM FileOperations");
            Assert.Contains(operations, new[] { "0|", "1|Committed" });
            AssertSentinelIntact();
        }
        finally
        {
            // Plain Win32 paths can't delete these names; remove them wherever they ended up.
            foreach (string leftover in Directory.EnumerateFiles(@"\\?\" + fixture.Paths.LibraryRoot, "*", SearchOption.AllDirectories)
                         .Where(path => Path.GetFileName(path) is "CON" or "x."))
            {
                File.Delete(leftover);
            }
        }
    }

    [Fact]
    public async Task UnknownTrashFoldersAreKeptForReview()
    {
        if (!OperatingSystem.IsWindows()) return;
        RemovalLibrary.WriteFile(Path.Combine(fixture.Paths.TrashRoot, "not-a-guid"), "x.txt", "unknown");
        RemovalLibrary.WriteFile(Path.Combine(fixture.Paths.TrashRoot, Guid.NewGuid().ToString("N").ToUpperInvariant()), "y.txt", "unknown");

        await fixture.RestartAsync();

        Assert.True(File.Exists(Path.Combine(fixture.Paths.TrashRoot, "not-a-guid", "x.txt")));
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.TrashRoot), path => Path.GetFileName(path).Any(char.IsUpper) && Path.GetFileName(path).Length == 32);
        Assert.True(fixture.Library.Repository.LastStartupReconciliation!.ReviewOrphanCount >= 2);
    }

    [Fact]
    public async Task AJunctionedArtworkFolderKeepsTheOutsideFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        string artwork = fixture.Export.ArtworkFile();
        string folder = Path.GetDirectoryName(artwork)!;
        string outsideCover = Path.Combine(outside, Path.GetFileName(artwork));
        File.Copy(artwork, outsideCover);
        Directory.Delete(folder, true);
        RemovalLibrary.CreateJunction(folder, outside);
        try
        {
            GameRemovalResult result = await fixture.GameRemover().RemoveAsync(fixture.SubjectGame, 2);

            Assert.Equal(GameRemovalOutcome.Removed, result.Outcome);
            Assert.True(File.Exists(outsideCover));
            AssertSentinelIntact();
        }
        finally
        {
            Directory.Delete(folder);
        }
    }
}
