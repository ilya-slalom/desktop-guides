using System.Security.Cryptography;
using System.Text;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class StaticHtmlDependencyScannerTests
{
    [Fact]
    public async Task NestedCssSrcSetAndCyclesProduceOneHashedAssetPerPath()
    {
        MemorySource source = new(
            ("guide.html", """
                <link rel="stylesheet" href="css/a.css?edition=1#main">
                <img src="images/one.png?edition=1#preview">
                <img srcset="images/one.png 1x, images/two.webp 2x">
                <div style="background-image: url('images/inline.gif')"></div>
                """),
            ("css/a.css", """
                @import "./b.css";
                .guide { background: url("base.png") }
                .cover { --cover-image: url("custom.png"); background: var(--cover-image) }
                """),
            ("css/b.css", """
                @import "a.css";
                .chapter { background-image: url("icon.jpg") }
                """),
            ("css/base.png", "base-image"),
            ("css/custom.png", "custom-image"),
            ("css/icon.jpg", "icon-image"),
            ("images/one.png", "one-image"),
            ("images/two.webp", "two-image"),
            ("images/inline.gif", "inline-image"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["css/a.css", "css/b.css", "css/base.png", "css/custom.png",
                "css/icon.jpg",
                "guide.html", "images/inline.gif", "images/one.png",
                "images/two.webp"],
            manifest.Assets.Select(asset => asset.RelativePath));
        Assert.Equal(1, source.Reads.Count(path => path == "css/a.css"));
        Assert.Equal(1, source.Reads.Count(path => path == "images/one.png"));
        Assert.All(manifest.References, reference =>
            Assert.Equal(StaticReferenceStatus.Included, reference.Status));
        StaticAsset one = Assert.Single(
            manifest.Assets, asset => asset.RelativePath == "images/one.png");
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(
                Encoding.UTF8.GetBytes("one-image"))),
            one.Sha256);
        Assert.Equal(
            manifest.Assets.Sum(asset => asset.ByteCount),
            manifest.TotalBytes);
    }

    [Fact]
    public async Task CssTextInStringsAndCommentsDoesNotBecomeAnAsset()
    {
        MemorySource source = new(
            ("guide.html", """
                <style>
                /* url(ghost.png) */
                .a { content: "url(fake.png)"; background: url(real.png) }
                </style>
                """),
            ("real.png", "real"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["guide.html", "real.png"],
            manifest.Assets.Select(asset => asset.RelativePath));
        Assert.Single(manifest.References);
    }

    [Fact]
    public async Task StyleElementImportsAndImageUrlsShareTheEntryRoot()
    {
        MemorySource source = new(
            ("guide.html", """
                <style>
                  @import url("css/extra.css");
                  .a { background: url("cover.png") }
                </style>
                """),
            ("css/extra.css", ".b { background: url('icon.gif') }"),
            ("cover.png", "cover"),
            ("css/icon.gif", "icon"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["cover.png", "css/extra.css", "css/icon.gif", "guide.html"],
            manifest.Assets.Select(asset => asset.RelativePath));
    }

    [Fact]
    public async Task NestedStyleRuleImageIsIncluded()
    {
        MemorySource source = new(
            ("guide.html", """
                <style>.chapter { & .map { background: url(map.png) } }</style>
                """),
            ("map.png", "map"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["guide.html", "map.png"],
            manifest.Assets.Select(asset => asset.RelativePath));
    }

    [Fact]
    public async Task EscapedUrlFunctionsInCssDeclarationsAreIncluded()
    {
        MemorySource source = new(
            ("guide.html", """
                <style>.map { --image: u\72l(map.png); background: var(--image) }</style>
                <div style="background: u\72l(inline.gif)"></div>
                """),
            ("map.png", "map"),
            ("inline.gif", "inline"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["guide.html", "inline.gif", "map.png"],
            manifest.Assets.Select(asset => asset.RelativePath));
    }

    [Fact]
    public async Task NamespaceAndConditionUrlsAreNotImageResources()
    {
        MemorySource source = new(
            ("guide.html", """
                <style>
                    @namespace url("private.png");
                    @supports (background: url(fake.png)) {
                        .map { background: url(map.png) }
                    }
                </style>
                """),
            ("private.png", "private"),
            ("fake.png", "fake"),
            ("map.png", "map"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["guide.html", "map.png"],
            manifest.Assets.Select(asset => asset.RelativePath));
    }

    [Fact]
    public async Task MissingAssetAfterAssetCapIsReportedAsMissing()
    {
        MemorySource source = new(
            ("guide.html", """
                <img src="one.png"><img src="missing.png">
                """),
            ("one.png", "one"));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner(
                new StaticHtmlScanLimits(MaxAssets: 1))
            .ScanAsync("guide.html", source);

        Assert.Equal(
            ["guide.html", "one.png"],
            manifest.Assets.Select(asset => asset.RelativePath));
        Assert.Equal(
            [StaticReferenceStatus.Included, StaticReferenceStatus.Missing],
            manifest.References.Select(reference => reference.Status));
    }

    [Fact]
    public async Task MissingRemoteUnsupportedAndUnsafeTargetsAreNotOpened()
    {
        MemorySource source = new(
            ("guide.html", """
                <img src="missing.png">
                <img src="https://example.test/tracker.png">
                <img src="../outside.png">
                <img src="bad.svg">
                <script src="https://example.test/evil.js"></script>
                <base href="https://example.test/">
                <link rel="icon" href="../favicon.ico">
                <a href="#chapter">Chapter</a>
                """));

        StaticHtmlManifest manifest = await new StaticHtmlDependencyScanner()
            .ScanAsync("guide.html", source);

        Assert.Equal(
            [StaticReferenceStatus.Missing, StaticReferenceStatus.Remote,
                StaticReferenceStatus.Unsafe, StaticReferenceStatus.Unsupported,
                StaticReferenceStatus.Remote, StaticReferenceStatus.Remote,
                StaticReferenceStatus.Unsafe],
            manifest.References.Select(reference => reference.Status));
        Assert.Equal(["guide.html", "missing.png"], source.Reads);
        Assert.All(
            manifest.References.Skip(4),
            reference => Assert.Equal(StaticAssetKind.Other, reference.ExpectedKind));
    }

    [Theory]
    [InlineData(StaticScanLimit.EntryBytes)]
    [InlineData(StaticScanLimit.AssetBytes)]
    [InlineData(StaticScanLimit.TotalBytes)]
    [InlineData(StaticScanLimit.AssetCount)]
    [InlineData(StaticScanLimit.ReferenceCount)]
    [InlineData(StaticScanLimit.CssDepth)]
    [InlineData(StaticScanLimit.CssRuleCount)]
    public async Task BudgetExceededReturnsItsSpecificLimit(StaticScanLimit limit)
    {
        MemorySource source = new(
            ("guide.html", """
                <link rel="stylesheet" href="a.css">
                <img src="one.png">
                <img src="two.png">
                """),
            ("a.css", """
                @import "b.css";
                .one { background: url(one.png) }
                .two { background: url(two.png) }
                """),
            ("b.css", ".third { color: red }"),
            ("one.png", "one"),
            ("two.png", "two"));
        StaticHtmlScanLimits limits = limit switch
        {
            StaticScanLimit.EntryBytes => new(MaxEntryBytes: 3),
            StaticScanLimit.AssetBytes => new(MaxAssetBytes: 3),
            StaticScanLimit.TotalBytes => new(MaxTotalBytes: 5),
            StaticScanLimit.AssetCount => new(MaxAssets: 1),
            StaticScanLimit.ReferenceCount => new(MaxReferences: 1),
            StaticScanLimit.CssDepth => new(MaxCssDepth: 1),
            StaticScanLimit.CssRuleCount => new(MaxCssRules: 1),
            _ => throw new ArgumentOutOfRangeException(nameof(limit))
        };

        StaticHtmlScanException error = await Assert.ThrowsAsync<
            StaticHtmlScanException>(() =>
                new StaticHtmlDependencyScanner(limits)
                    .ScanAsync("guide.html", source));

        Assert.Equal(limit, error.Limit);
    }

    private sealed class MemorySource(params (string Path, string Text)[] files)
        : IStaticHtmlAssetSource
    {
        private readonly Dictionary<string, byte[]> content = files.ToDictionary(
            file => file.Path,
            file => Encoding.UTF8.GetBytes(file.Text),
            StringComparer.Ordinal);

        public List<string> Reads { get; } = [];

        public ValueTask<Stream?> OpenReadAsync(
            string safeRelativePath,
            CancellationToken cancellationToken = default)
        {
            Reads.Add(safeRelativePath);
            Stream? stream = content.TryGetValue(safeRelativePath, out byte[]? bytes)
                ? new MemoryStream(bytes, writable: false)
                : null;
            return ValueTask.FromResult(stream);
        }
    }
}
