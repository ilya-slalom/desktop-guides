using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using DesktopGuides.Infrastructure.Tests.Import;

namespace DesktopGuides.Infrastructure.Tests.FaultInjection;

internal sealed class InjectedFault() : Exception("Injected fault.");

/// <summary>
/// T20.1's populated library plus a bystander game, an HTML import source,
/// and publishers and removers with fault hooks. The subject game holds the
/// TXT guide (the guide-removal subject, also LastActiveGuideId) and its HTML
/// sibling, with artwork. The bystander game has its own artwork, guide,
/// reading state and preference; no cell may touch it.
/// </summary>
internal sealed class FaultFixture : IAsyncDisposable
{
    private FaultFixture(ExportFixture export) => Export = export;

    public ExportFixture Export { get; }
    public RemovalLibrary Library => Export.Library;
    public ManagedPathResolver Paths => Export.Library.Paths;
    public Guid SubjectGame => Export.LinkedGame;
    public Guid SubjectGuide => Export.TxtGuide;
    public Guid SiblingGuide => Export.HtmlGuide;
    public Guid BystanderGame { get; private set; }
    public Guid BystanderGuide { get; private set; }
    public ImportTestDirectory Sources { get; } = new();
    public ImportManifest HtmlManifest { get; private set; } = null!;
    public Guid ImportGuideId { get; private set; }

    public static async Task<FaultFixture> CreateAsync()
    {
        FaultFixture fixture = new(await ExportFixture.CreateAsync());
        Guid bystander = Guid.NewGuid();
        StoredArtwork cover = await fixture.Export.Store.StoreAsync(bystander, Artwork.TestImages.Png(6, 6), default);
        fixture.BystanderGame = (await fixture.Library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            bystander, "Bystander Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, "900700", DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath))).Id;
        fixture.BystanderGuide = await fixture.Export.AddFileGuideAsync(
            fixture.BystanderGame, "Bystander Guide", GuideFormat.Txt, "guide.txt", "bystander"u8.ToArray());
        fixture.Library.Execute($$"""
            UPDATE ReadingStates SET EstimatedFraction = 0.25 WHERE GuideId = '{{fixture.BystanderGuide:N}}';
            UPDATE ReaderPreferences SET TextScale = 1.5 WHERE GuideId = '{{fixture.BystanderGuide:N}}';
            INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('LastActiveGuideId', '{{fixture.SubjectGuide:N}}');
            """);
        foreach (string file in new[] { "images/map.png", "styles/main.css", "styles/palette.css" })
        {
            fixture.Sources.Copy("html-static/" + file, file);
        }
        string entry = fixture.Sources.Copy("html-static/guide.html", "guide.html");
        fixture.HtmlManifest = ((ImportReady)await new GuideImportValidator().InspectAsync(entry, CancellationToken.None)).Manifest;
        return fixture;
    }

    /// <summary>A publisher whose next import uses fresh, known IDs.</summary>
    public GuideImportPublisher Publisher(
        Action<ImportCheckpoint>? checkpoint = null,
        Func<string, Stream>? createStagedFile = null,
        Action<IImportJournal, Guid>? rollBack = null)
    {
        Guid operationId = Guid.NewGuid();
        ImportGuideId = Guid.NewGuid();
        Queue<Guid> ids = new([operationId, ImportGuideId]);
        return new GuideImportPublisher(
            Library.Repository, Paths, null,
            () => ids.Count > 0 ? ids.Dequeue() : Guid.NewGuid(),
            createStagedFile ?? (path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)),
            checkpoint ?? (_ => { }),
            rollBack ?? ((journal, operation) => journal.RollBack(operation)));
    }

    public Task<Guid> ImportAsync(
        GuideImportPublisher publisher, CancellationToken token = default, IProgress<ImportProgress>? progress = null) =>
        publisher.PublishAsync(HtmlManifest, SubjectGame, "Imported Guide", false, progress, token);

    public GuideRemover GuideRemover(
        Action<RemovalCheckpoint>? checkpoint = null,
        Action<IDeletionJournal, Guid>? rollBack = null,
        Action<IDeletionJournal, Guid>? finish = null) =>
        new(Library.Repository, Paths, checkpoint ?? (_ => { }), rollBack, finish);

    public GameRemover GameRemover(
        Action<RemovalCheckpoint>? checkpoint = null,
        Action<IDeletionJournal, Guid>? rollBack = null,
        Action<IDeletionJournal, Guid>? finish = null) =>
        new(Library.Repository, Paths, Export.Store, checkpoint ?? (_ => { }), rollBack, finish);

    public LibrarySnapshot Capture() => LibrarySnapshot.Capture(Paths);

    /// <summary>A fresh repository on the same files: startup recovery runs.</summary>
    public Task RestartAsync() => Library.RestartAsync();

    public static Action<T> FaultAt<T>(T point) where T : struct, Enum => reached =>
    {
        if (EqualityComparer<T>.Default.Equals(reached, point)) throw new InjectedFault();
    };

    public static Action<T> CancelAt<T>(T point, CancellationTokenSource source) where T : struct, Enum => reached =>
    {
        if (EqualityComparer<T>.Default.Equals(reached, point)) source.Cancel();
    };

    public static Action<IDeletionJournal, Guid> SkipDeletionRollBack => (_, _) => { };
    public static Action<IDeletionJournal, Guid> FailDeletionRollBack => (_, _) => throw new InjectedFault();
    public static Action<IDeletionJournal, Guid> SkipFinish => (_, _) => { };
    public static Action<IImportJournal, Guid> SkipImportRollBack => (_, _) => { };

    public async ValueTask DisposeAsync()
    {
        Sources.Dispose();
        await Export.DisposeAsync();
    }
}
