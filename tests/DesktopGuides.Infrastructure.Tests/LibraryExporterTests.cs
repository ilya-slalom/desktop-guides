using System.Diagnostics;
using System.IO.Compression;
using DesktopGuides.Core.Backup;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibraryExporterTests : IAsyncLifetime
{
    private static readonly Guid ExportId = Guid.Parse("0f3c9a1e5b7d4c2a8e6f1b3d5a7c9e0f");
    private ExportFixture fixture = null!;
    private string output = null!;

    public async Task InitializeAsync()
    {
        fixture = await ExportFixture.CreateAsync();
        output = Path.Combine(fixture.Library.Root, "out");
        Directory.CreateDirectory(output);
    }

    public async Task DisposeAsync() => await fixture.DisposeAsync();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Recorder : IProgress<LibraryExportProgress>
    {
        public List<LibraryExportProgress> Reports { get; } = [];
        public void Report(LibraryExportProgress value) { lock (Reports) Reports.Add(value); }
    }

    private LibraryExporter Exporter(
        Action<ExportCheckpoint>? checkpoint = null, TimeProvider? clock = null, params string[] protectedRoots) =>
        new(fixture.Library.Repository, fixture.Library.Paths,
            new LibraryExportOptions("1.0.0.0", "msix", protectedRoots),
            clock, checkpoint ?? (_ => { }), () => ExportId);

    private string Destination(string name = "backup.zip") => Path.Combine(output, name);

    private Task<LibraryExportResult> Export(LibraryExporter? exporter = null, string? destination = null,
        bool overwrite = false, CancellationToken token = default) =>
        (exporter ?? Exporter()).ExportAsync(destination ?? Destination(), overwrite, null, token);

    private static IReadOnlyList<string> EntryNames(string zip)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        return archive.Entries.Select(entry => entry.FullName).ToArray();
    }

    private IReadOnlyList<string> ExpectedEntryNames() =>
        new[] { LibraryArchiveManifest.EntryName }
            .Concat(fixture.ExpectedArchivePaths().Append(LibraryArchiveManifest.DatabasePath)
                .OrderBy(path => path, StringComparer.Ordinal))
            .ToArray();

    private string Scalar(string zip, string sql)
    {
        string copy = Path.Combine(fixture.Library.Root, $"read-{Guid.NewGuid():N}.sqlite");
        using (ZipArchive archive = ZipFile.OpenRead(zip))
        {
            archive.GetEntry(LibraryArchiveManifest.DatabasePath)!.ExtractToFile(copy);
        }
        using SqliteConnection connection = new($"Data Source={copy};Pooling=False");
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar())!;
    }

    private void AssertNothingLeft()
    {
        Assert.Empty(Directory.EnumerateFiles(output, "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(Path.GetTempPath(), $"desktop-guides-export-{ExportId:N}.sqlite*"));
    }

    private async Task<LibraryExportException> Fails(LibraryExportIssue issue, LibraryExporter? exporter = null,
        string? destination = null, bool overwrite = false)
    {
        LibraryExportException error = await Assert.ThrowsAsync<LibraryExportException>(() =>
            Export(exporter, destination, overwrite));
        Assert.Equal(issue, error.Issue);
        return error;
    }

    [Fact]
    public async Task TheArchiveHoldsExactlyTheReferencedEntriesAndVerifies()
    {
        LibraryExportResult result = await Export();

        Assert.Equal(Destination(), result.Path);
        Assert.Equal(ExpectedEntryNames(), EntryNames(result.Path));
        Assert.Equal((2, 3, 7), (result.Games, result.Guides, result.Files));
        Assert.Equal(new FileInfo(result.Path).Length, result.Bytes);
        await using FileStream stream = File.OpenRead(result.Path);
        Assert.Equal(ExportId, LibraryArchiveVerifier.Verify(stream, CancellationToken.None).ExportId);
        AssertNothingLeft();
    }

    [Fact]
    public async Task TheSnapshotHoldsTheLibraryRows()
    {
        string zip = (await Export()).Path;

        Assert.Equal("2|3|3", Scalar(zip,
            "SELECT (SELECT COUNT(*) FROM Games) || '|' || (SELECT COUNT(*) FROM Guides) || '|' || (SELECT COUNT(*) FROM GuideAssets)"));
        Assert.Equal("0.5", Scalar(zip, $"SELECT EstimatedFraction FROM ReadingStates WHERE GuideId = '{fixture.TxtGuide:N}'"));
        Assert.Equal("1.25", Scalar(zip, $"SELECT TextScale FROM ReaderPreferences WHERE GuideId = '{fixture.HtmlGuide:N}'"));
        Assert.Equal("Dark", Scalar(zip, "SELECT Value FROM Settings WHERE Key = 'Theme'"));
        Assert.Equal("1", Scalar(zip, "SELECT COUNT(*) FROM Games WHERE MetadataJson IS NOT NULL AND ArtworkRelativePath IS NOT NULL"));
    }

    [Fact]
    public async Task NothingOutsideTheReferencedLibraryIsArchived()
    {
        ManagedPathResolver paths = fixture.Library.Paths;
        RemovalLibrary.WriteFile(paths.DataRoot, "providers.bin", "credentials");
        RemovalLibrary.WriteFile(paths.DataRoot, "providers.bin.tmp", "credentials");
        RemovalLibrary.WriteFile(paths.DataRoot, "library.session.lock", "");
        RemovalLibrary.WriteFile(paths.RecoveryRoot, "library-v3-old.sqlite", "old");
        RemovalLibrary.WriteFile(paths.StagingRoot, $"{Guid.NewGuid():N}/{Guid.NewGuid():N}/x.txt", "staged");
        RemovalLibrary.WriteFile(paths.TrashRoot, $"{Guid.NewGuid():N}/{Guid.NewGuid():N}/x.txt", "trashed");
        RemovalLibrary.WriteFile(paths.ArtworkStagingRoot, $"{Guid.NewGuid():N}.tmp", "staged cover");
        RemovalLibrary.WriteFile(paths.DataRoot, "Cache/WebView2/profile/x.bin", "profile");
        RemovalLibrary.WriteFile(paths.DataRoot, "Cache/diagnostics/html-session.json", "{}");
        RemovalLibrary.WriteFile(fixture.Content(fixture.TxtGuide), "stray.txt", "not referenced");

        string zip = (await Export()).Path;

        Assert.Equal(ExpectedEntryNames(), EntryNames(zip));
    }

    [Fact]
    public async Task AnEditDuringTheExportWaitsAndIsNotInTheSnapshot()
    {
        Task<Game>? late = null;
        LibraryExporter exporter = Exporter(point =>
        {
            if (point == ExportCheckpoint.GateHeld) late = fixture.Library.Repository.AddGameAsync("Late", null, null);
        });

        string zip = (await Export(exporter)).Path;

        Assert.Equal("2", Scalar(zip, "SELECT COUNT(*) FROM Games"));
        Assert.Equal("Late", (await late!).Title);
        Assert.Equal("3", fixture.Library.Scalar("SELECT COUNT(*) FROM Games"));
    }

    [Fact]
    public async Task ALeftoverPreparedImportIsReconciledBeforeTheSnapshot()
    {
        Guid operation = Guid.NewGuid();
        Guid guide = Guid.NewGuid();
        await fixture.Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, guide);
            return Task.FromResult(true);
        }, CancellationToken.None);
        RemovalLibrary.WriteFile(fixture.Library.Paths.GetStagedGuideRoot(operation, guide), "x.txt", "half copied");

        string zip = (await Export()).Path;

        Assert.Equal("0", Scalar(zip, "SELECT COUNT(*) FROM FileOperations"));
        Assert.Equal("0", fixture.Library.Scalar("SELECT COUNT(*) FROM FileOperations"));
        Assert.False(Directory.Exists(fixture.Library.Paths.GetStagedGuideRoot(operation, guide)));
    }

    [Fact]
    public async Task AnOperationRecoveryCannotFinishFailsTheExport()
    {
        fixture.Library.Execute($"""
            INSERT INTO FileOperations (Id, Kind, Phase, ManifestJson, CreatedUtcMs)
            VALUES ('{Guid.NewGuid():N}', 'Import', 'Prepared', 'not json', 0)
            """);

        await Fails(LibraryExportIssue.RecoveryIncomplete);

        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task AGuideFileChangedInPlaceIsDamaged()
    {
        string file = Path.Combine(fixture.Content(fixture.TxtGuide), "guide.txt");
        File.WriteAllText(file, File.ReadAllText(file).Replace("one", "One"));

        LibraryExportException error = await Fails(LibraryExportIssue.ManagedFilesDamaged);

        Assert.Equal(new[] { fixture.TxtGuide }, error.GuideIds);
        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task ArtworkWhoseBytesDoNotMatchItsNameIsDamaged()
    {
        byte[] bytes = File.ReadAllBytes(fixture.ArtworkFile());
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(fixture.ArtworkFile(), bytes);

        LibraryExportException error = await Fails(LibraryExportIssue.ManagedFilesDamaged);

        Assert.Equal(new[] { fixture.LinkedGame }, error.GameIds);
        AssertNothingLeft();
    }

    [Fact]
    public async Task DestinationRulesCompareWholeSegments()
    {
        ManagedPathResolver paths = fixture.Library.Paths;
        string package = Path.Combine(fixture.Library.Root, "package");
        Directory.CreateDirectory(Path.Combine(package, "LocalCache"));
        string sibling = paths.DataRoot + "X";
        Directory.CreateDirectory(sibling);
        LibraryExporter exporter = Exporter(protectedRoots: package);

        await Fails(LibraryExportIssue.DestinationNotAllowed, exporter, Path.Combine(paths.DataRoot, "backup.zip"));
        await Fails(LibraryExportIssue.DestinationNotAllowed, exporter, Path.Combine(paths.LibraryRoot, "backup.zip"));
        await Fails(LibraryExportIssue.DestinationNotAllowed, exporter, Path.Combine(package, "LocalCache", "backup.zip"));
        Assert.True(File.Exists((await Export(exporter, Path.Combine(sibling, "backup.zip"))).Path));
    }

    [Fact]
    public async Task TheDestinationMustBeAZipPathInAnExistingFolder()
    {
        await Fails(LibraryExportIssue.DestinationUnavailable, destination: Path.Combine(output, "missing", "backup.zip"));
        await Fails(LibraryExportIssue.DestinationUnavailable, destination: Destination("backup.txt"));
        await Fails(LibraryExportIssue.DestinationUnavailable, destination: "backup.zip");
        AssertNothingLeft();
    }

    [Fact]
    public async Task AnExistingDestinationIsKeptWithoutOverwriteAndReplacedWithIt()
    {
        File.WriteAllText(Destination(), "older backup");

        await Fails(LibraryExportIssue.DestinationExists);
        Assert.Equal("older backup", File.ReadAllText(Destination()));

        await Export(overwrite: true);
        Assert.Equal(ExpectedEntryNames(), EntryNames(Destination()));
    }

    [Theory]
    [InlineData("GateHeld")]
    [InlineData("Snapshotted")]
    [InlineData("Planned")]
    [InlineData("EntryWritten")]
    [InlineData("Written")]
    [InlineData("BeforeRename")]
    public async Task CancellingAtAnyStageLeavesNothingAndReleasesTheGate(string stage)
    {
        ExportCheckpoint target = Enum.Parse<ExportCheckpoint>(stage);
        using CancellationTokenSource cancel = new();
        LibraryExporter exporter = Exporter(point =>
        {
            if (point == target) cancel.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Export(exporter, token: cancel.Token));

        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
        Assert.Equal("After", (await fixture.Library.Repository.AddGameAsync("After", null, null)).Title);
    }

    [Fact]
    public async Task CancellingWhileWaitingForTheGateLeavesNothing()
    {
        TaskCompletionSource release = new();
        Task<bool> holder = fixture.Library.Repository.RunExportAsync(async (_, _) =>
        {
            await release.Task;
            return true;
        }, CancellationToken.None);
        using CancellationTokenSource cancel = new();

        Task<LibraryExportResult> waiting = Export(token: cancel.Token);
        await Task.Delay(100);
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();
        await holder;
        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task ACorruptedTemporaryArchiveFailsVerification()
    {
        LibraryExporter exporter = Exporter(point =>
        {
            if (point != ExportCheckpoint.Written) return;
            string temp = Directory.EnumerateFiles(output, "*.tmp").Single();
            using FileStream stream = new(temp, FileMode.Open, FileAccess.ReadWrite);
            stream.Position = stream.Length / 2;
            stream.WriteByte((byte)(stream.ReadByte() ^ 0xFF));
        });

        await Fails(LibraryExportIssue.VerificationFailed, exporter);

        Assert.False(File.Exists(Destination()));
        AssertNothingLeft();
    }

    [Fact]
    public async Task TwoExportsWithTheSameClockAreByteIdentical()
    {
        FixedClock clock = new(new DateTimeOffset(2026, 10, 8, 7, 12, 34, 500, TimeSpan.Zero));

        string first = (await Export(Exporter(clock: clock), Destination("first.zip"))).Path;
        string second = (await Export(Exporter(clock: clock), Destination("second.zip"))).Path;

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        using ZipArchive archive = ZipFile.OpenRead(first);
        Assert.All(archive.Entries, entry => Assert.Equal(new DateTime(2026, 10, 8, 7, 12, 34), entry.LastWriteTime.DateTime));
    }

    [Fact]
    public async Task StaleTempFromAnotherExportIsLeftAlone()
    {
        string stale = Destination($"backup.zip.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(stale, "crashed export");

        await Export();

        Assert.Equal("crashed export", File.ReadAllText(stale));
        File.Delete(stale);
    }

    [Fact]
    public async Task EmptyLibraryExportsTheDatabaseOnly()
    {
        await using RemovalLibrary empty = await RemovalLibrary.CreateAsync();
        LibraryExporter exporter = new(empty.Repository, empty.Paths,
            new LibraryExportOptions("1.0.0.0", "portable", []), null, _ => { }, () => ExportId);

        LibraryExportResult result = await exporter.ExportAsync(Destination("empty.zip"), false, null, default);

        Assert.Equal(new[] { LibraryArchiveManifest.EntryName, LibraryArchiveManifest.DatabasePath }, EntryNames(result.Path));
        Assert.Equal((0, 0, 1), (result.Games, result.Guides, result.Files));
    }

    [Fact]
    public async Task ProgressRunsThroughEachPhaseToTheTotal()
    {
        Recorder recorder = new();

        await Exporter().ExportAsync(Destination(), false, recorder, default);

        Assert.Equal(
            new[] { LibraryExportPhase.Preparing, LibraryExportPhase.Writing, LibraryExportPhase.Verifying },
            recorder.Reports.Select(report => report.Phase).Distinct());
        LibraryExportProgress written = recorder.Reports.Last(report => report.Phase == LibraryExportPhase.Writing);
        Assert.True(written.BytesTotal > 0);
        Assert.Equal(written.BytesTotal, written.BytesDone);
    }

    private sealed class CallerContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SynchronizationContext? previous = Current;
                SetSynchronizationContext(this);
                try { callback(state); }
                finally { SetSynchronizationContext(previous); }
            });
    }

    [Fact]
    public async Task VerificationAndRenameDoNotRunOnTheCallersContext()
    {
        CallerContext caller = new();
        bool onCaller = true;
        LibraryExporter exporter = Exporter(point =>
        {
            if (point == ExportCheckpoint.BeforeRename) onCaller = SynchronizationContext.Current == caller;
        });
        TaskCompletionSource<LibraryExportResult> done = new();
        caller.Post(async _ =>
        {
            try { done.SetResult(await Export(exporter)); }
            catch (Exception error) { done.SetException(error); }
        }, null);

        await done.Task;

        Assert.False(onCaller);
    }

    [Fact]
    public async Task AHardLinkedLiveDatabaseFailsAsDatabaseInvalid()
    {
        if (!OperatingSystem.IsWindows()) return;
        string link = Path.Combine(fixture.Library.Root, "linked.sqlite");
        using (Process mklink = Process.Start(new ProcessStartInfo(
            "cmd.exe", $"/c mklink /H \"{link}\" \"{fixture.Library.Paths.DatabasePath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!)
        {
            mklink.WaitForExit();
            Assert.Equal(0, mklink.ExitCode);
        }
        try
        {
            await Fails(LibraryExportIssue.DatabaseInvalid);
            AssertNothingLeft();
        }
        finally
        {
            File.Delete(link);
        }
    }

    [Fact]
    public async Task AReaderHoldingTheTemporaryArchiveDoesNotFailVerification()
    {
        FileStream? scanner = null;
        LibraryExporter exporter = Exporter(point =>
        {
            if (point == ExportCheckpoint.Written)
            {
                scanner = new FileStream(Directory.EnumerateFiles(output, "*.tmp").Single(),
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            if (point == ExportCheckpoint.BeforeRename) scanner?.Dispose();
        });

        try
        {
            Assert.True(File.Exists((await Export(exporter)).Path));
        }
        finally
        {
            scanner?.Dispose();
        }
    }
}
