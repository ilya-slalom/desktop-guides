using System.Security.Cryptography;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Import;
using DesktopGuides.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace DesktopGuides.Infrastructure.Tests;

/// <summary>
/// A populated library for the export tests: a linked game with IGDB
/// metadata and artwork, a plain game, and a TXT, an HTML and a PDF guide,
/// plus reading state, a preference and a setting.
/// </summary>
internal sealed class ExportFixture : IAsyncDisposable
{
    public const string HtmlFolder = "Café – v2_files";

    private ExportFixture(RemovalLibrary library)
    {
        Library = library;
        Store = new ManagedArtworkStore(library.Paths);
    }

    public RemovalLibrary Library { get; }
    public ManagedArtworkStore Store { get; }
    public Guid LinkedGame { get; private set; }
    public Guid PlainGame { get; private set; }
    public Guid TxtGuide { get; private set; }
    public Guid HtmlGuide { get; private set; }
    public Guid PdfGuide { get; private set; }
    public string ArtworkRelativePath { get; private set; } = "";

    public static async Task<ExportFixture> CreateAsync()
    {
        ExportFixture fixture = new(await RemovalLibrary.CreateAsync());
        await fixture.PopulateAsync();
        return fixture;
    }

    private async Task PopulateAsync()
    {
        Guid linked = Guid.NewGuid();
        StoredArtwork cover = await Store.StoreAsync(linked, Artwork.TestImages.Png(4, 4), default);
        ArtworkRelativePath = cover.RelativePath;
        LinkedGame = (await Library.Repository.AddLinkedGameAsync(new NewLinkedGame(
            linked, "Linked Game", "PC",
            new ProviderGameLink(ProviderGameLink.Igdb, "900500", DateTimeOffset.UnixEpoch),
            GameMetadataJsonTests.Sample(), cover.RelativePath))).Id;
        PlainGame = await Library.AddGameAsync("Plain Game");
        TxtGuide = await AddFileGuideAsync(LinkedGame, "Walkthrough", GuideFormat.Txt, "guide.txt",
            "Step one.\nStep two.\n"u8.ToArray());
        PdfGuide = await AddFileGuideAsync(PlainGame, "Manual", GuideFormat.Pdf, "manual.pdf",
            "%PDF-1.4 export fixture"u8.ToArray());
        HtmlGuide = await AddHtmlGuideAsync(LinkedGame, "Maps",
            ("guide.html", "guide.html", GuideAssetKind.EntryHtml, "<html>maps</html>"u8.ToArray()),
            ($"{HtmlFolder}/style.css", $"{HtmlFolder}/style.css", GuideAssetKind.StyleSheet, "body{}"u8.ToArray()),
            ($"{HtmlFolder}/b.png", $"{HtmlFolder}/b.png", GuideAssetKind.Image, Artwork.TestImages.Png(2, 2)));
        Library.Execute($$"""
            UPDATE ReadingStates SET LocatorJson = '{"format":"Txt"}', EstimatedFraction = 0.5,
                LastOpenedUtcMs = 1000 WHERE GuideId = '{{TxtGuide:N}}';
            UPDATE ReaderPreferences SET TextScale = 1.25 WHERE GuideId = '{{HtmlGuide:N}}';
            INSERT OR REPLACE INTO Settings (Key, Value) VALUES ('Theme', 'Dark');
            """);
    }

    public string Content(Guid guide) => Library.Paths.GetGuideRoot(guide);

    public string ArtworkFile() => Store.ResolveFile(ArtworkRelativePath)!;

    /// <summary>A validated copy of the live database, as export takes it.</summary>
    public string Snapshot()
    {
        string path = Path.Combine(Library.Root, $"snapshot-{Guid.NewGuid():N}.sqlite");
        using SqliteConnection live = new($"Data Source={Library.Paths.DatabasePath};Pooling=False");
        live.Open();
        SqliteLibraryRepository.BackupTo(live, path, 4);
        return path;
    }

    public IReadOnlyList<string> ExpectedArchivePaths() =>
        new[]
        {
            "library/" + ArtworkRelativePath,
            $"library/content/{TxtGuide:N}/guide.txt",
            $"library/content/{PdfGuide:N}/manual.pdf",
            $"library/content/{HtmlGuide:N}/guide.html",
            $"library/content/{HtmlGuide:N}/{HtmlFolder}/style.css",
            $"library/content/{HtmlGuide:N}/{HtmlFolder}/b.png",
        }.OrderBy(path => path, StringComparer.Ordinal).ToArray();

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public async Task<Guid> AddFileGuideAsync(
        Guid game, string title, GuideFormat format, string name, byte[] bytes)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        await Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, id);
            Write(id, name, bytes);
            journal.Publish(new NewImportedGuide(
                operation, id, game, title, format, name, Sha(bytes), bytes.Length, name, null), () => { });
            return Task.FromResult(true);
        }, CancellationToken.None);
        return id;
    }

    public async Task<Guid> AddHtmlGuideAsync(
        Guid game, string title, params (string Request, string Relative, GuideAssetKind Kind, byte[] Bytes)[] files)
    {
        Guid operation = Guid.NewGuid();
        Guid id = Guid.NewGuid();
        GuideAsset[] assets = files
            .Select(file => new GuideAsset(file.Request, file.Relative, file.Kind, file.Bytes.Length, Sha(file.Bytes)))
            .ToArray();
        await Library.Repository.RunImportAsync<bool>((journal, _) =>
        {
            journal.Prepare(operation, id);
            foreach ((string Request, string Relative, GuideAssetKind Kind, byte[] Bytes) file in files)
            {
                Write(id, file.Relative, file.Bytes);
            }
            journal.Publish(new NewImportedGuide(
                operation, id, game, title, GuideFormat.Html, files[0].Relative,
                GuideFingerprint.OfHtml(assets.Select(asset => (asset.RelativePath, asset.Sha256))),
                assets.Sum(asset => asset.ByteCount), title + ".html", null, assets), () => { });
            return Task.FromResult(true);
        }, CancellationToken.None);
        return id;
    }

    private void Write(Guid guide, string relative, byte[] bytes)
    {
        string path = Path.Combine(Library.Paths.GetGuideRoot(guide), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    public ValueTask DisposeAsync() => Library.DisposeAsync();
}
