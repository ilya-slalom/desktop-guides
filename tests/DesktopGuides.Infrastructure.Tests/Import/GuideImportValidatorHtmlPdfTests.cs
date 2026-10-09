using System.Security.Cryptography;
using DesktopGuides.Core.Import;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideImportValidatorHtmlPdfTests
{
    private static readonly string[] KnownWarnings =
    [
        "A local asset is missing and will not appear offline.",
        "A remote asset will be blocked in the offline reader.",
        "An unsafe asset path will be blocked.",
        "An unsupported asset will be blocked.",
    ];

    private static Task<ImportInspection> Inspect(string path, GuideImportLimits? limits = null) =>
        new GuideImportValidator(limits).InspectAsync(path, CancellationToken.None);

    private static async Task<GuideImportException> Rejected(string path, GuideImportLimits? limits = null) =>
        await Assert.ThrowsAsync<GuideImportException>(() => Inspect(path, limits));

    private static async Task<T> Manifest<T>(string path, GuideImportLimits? limits = null)
        where T : ImportManifest =>
        Assert.IsType<T>(Assert.IsType<ImportReady>(await Inspect(path, limits)).Manifest);

    [Fact]
    public async Task HtmlManifestCarriesTheContentFingerprint()
    {
        if (!OperatingSystem.IsWindows()) return;

        HtmlImportManifest html = await Manifest<HtmlImportManifest>(
            P0Fixtures.Resolve("html-static/guide.html"));

        Assert.Equal(
            "743c87a4c5222c99cb5de3f4ee931a63b994a54be3dc9ec5855c855dafcd3f21",
            html.Fingerprint);
    }

    [Fact]
    public async Task StaticHtmlMapsEntryAssetsAndSize()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = P0Fixtures.Resolve("html-static");

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(Path.Combine(root, "guide.html"));

        Assert.Equal("guide.html", manifest.EntryRelativePath);
        Assert.Equal(3, manifest.AssetCount);
        long expected = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        Assert.Equal(expected, manifest.TotalBytes);
        Assert.Empty(manifest.Warnings);
        Assert.Equal("guide", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task HostileHtmlIsReadyWithWarnings()
    {
        if (!OperatingSystem.IsWindows()) return;

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(
            P0Fixtures.Resolve("html-hostile/guide.html"));

        Assert.True(manifest.Warnings.Count >= 2);
        Assert.Contains(manifest.Warnings, w => w.Message == KnownWarnings[1]);
        Assert.All(manifest.Warnings, w =>
        {
            Assert.Contains(w.Message, KnownWarnings);
            Assert.False(string.IsNullOrEmpty(w.RelativePath));
        });
    }

    [Fact]
    public async Task PercentNamedEntryKeepsTitleAndMapsEntry()
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();
        string entry = files.Copy("html-static/guide.html", "100% Walkthrough.html");
        files.Copy("html-static/images/map.png", "images/map.png");
        files.Copy("html-static/styles/main.css", "styles/main.css");
        files.Copy("html-static/styles/palette.css", "styles/palette.css");

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(entry);

        Assert.Equal("guide.html", manifest.EntryRelativePath);
        Assert.Equal("100% Walkthrough", manifest.SuggestedTitle);
        Assert.Equal("100% Walkthrough.html", manifest.Source.FileName);
        Assert.Equal(3, manifest.AssetCount);
    }

    [Theory]
    [InlineData("GUIDE.HTM")]
    [InlineData("Guide.Html")]
    public async Task UpperCaseHtmlExtensionsAreAccepted(string name)
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(
            files.Write(name, "<p>Route</p>"));

        Assert.Equal(0, manifest.AssetCount);
    }

    [Fact]
    public async Task CaseCollisionIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html", "<img src=\"map.png\"><img src=\"MAP.png\">");
        files.Write("map.png", "image");

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("The guide's folder has files whose names differ only by case.", error.Message);
    }

    [Fact]
    public async Task LinkLeavingTheFolderIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory outside = new();
        using ImportTestDirectory files = new();
        string target = outside.Write("secret.png", "outside");
        string entry = files.Write("guide.html", "<img src=\"linked.png\">");
        File.CreateSymbolicLink(Path.Combine(files.Root, "linked.png"), target);

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("The guide refers to a file outside its folder.", error.Message);
    }

    public static TheoryData<StaticScanLimit, string> HtmlLimitCases => new()
    {
        { StaticScanLimit.EntryBytes, "The web page is larger than the 3 bytes limit." },
        { StaticScanLimit.AssetBytes, "A linked file is larger than the 3 bytes limit for one file." },
        { StaticScanLimit.TotalBytes, "The web page and its linked files are larger than the 5 bytes limit." },
        { StaticScanLimit.AssetCount, "The web page links more than 1 files." },
        { StaticScanLimit.ReferenceCount, "The web page has more than 1 links to other files." },
        { StaticScanLimit.CssDepth, "The web page's style sheets nest more than 1 levels deep." },
        { StaticScanLimit.CssRuleCount, "The web page's style sheets have more than 1 rules." },
    };

    [Theory]
    [MemberData(nameof(HtmlLimitCases))]
    public async Task HtmlLimitsMapToTooLarge(StaticScanLimit limit, string message)
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html",
            "<link rel=\"stylesheet\" href=\"a.css\">\n<img src=\"one.png\">\n<img src=\"two.png\">");
        files.Write("a.css",
            "@import \"b.css\";\n.one { background: url(one.png) }\n.two { background: url(two.png) }");
        files.Write("b.css", ".third { color: red }");
        files.Write("one.png", "one");
        files.Write("two.png", "two");
        StaticHtmlScanLimits html = limit switch
        {
            StaticScanLimit.EntryBytes => new(MaxEntryBytes: 3),
            StaticScanLimit.AssetBytes => new(MaxAssetBytes: 3),
            StaticScanLimit.TotalBytes => new(MaxTotalBytes: 5),
            StaticScanLimit.AssetCount => new(MaxAssets: 1),
            StaticScanLimit.ReferenceCount => new(MaxReferences: 1),
            StaticScanLimit.CssDepth => new(MaxCssDepth: 1),
            _ => new(MaxCssRules: 1),
        };

        GuideImportException error = await Rejected(entry, new GuideImportLimits(Html: html));

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task HtmlEntryAtTheLimitPassesAndOneByteMoreIsTooLarge()
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html", "<p>Route</p>");
        long size = new FileInfo(entry).Length;

        await Manifest<HtmlImportManifest>(entry,
            new GuideImportLimits(Html: new StaticHtmlScanLimits(MaxEntryBytes: size)));
        GuideImportException error = await Rejected(entry,
            new GuideImportLimits(Html: new StaticHtmlScanLimits(MaxEntryBytes: size - 1)));

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.Equal($"The web page is larger than the {size - 1} bytes limit.", error.Message);
    }

    [Fact]
    public async Task ManyRemoteReferencesAllComeThrough()
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();
        string body = string.Concat(Enumerable.Range(0, 250)
            .Select(n => $"<img src=\"https://cdn.example.com/maps/area-{n}.png\">\n"));
        string entry = files.Write("guide.html", body);

        HtmlImportManifest manifest = await Manifest<HtmlImportManifest>(entry);

        Assert.Equal(250, manifest.Warnings.Count);
        Assert.All(manifest.Warnings, w => Assert.Equal(KnownWarnings[1], w.Message));
    }

    [Fact]
    public async Task HtmlNameEndingInASpaceBeforeTheExtensionIsExplained()
    {
        using ImportTestDirectory files = new();
        string entry = files.Write("Sonic .html", "<p>Route</p>");

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("Sonic .html can't be used as a guide file name. Rename it and import again.", error.Message);
    }

    [Fact]
    public async Task UndecodableStyleSheetIsExplained()
    {
        if (!OperatingSystem.IsWindows()) return;

        using ImportTestDirectory files = new();
        string entry = files.Write("guide.html", "<link rel=\"stylesheet\" href=\"main.css\"><p>Route</p>");
        // Declares UTF-8 but holds a Windows-1252 "©" (0xA9).
        files.Write("main.css", [.. "@charset \"utf-8\"; /* "u8, 0xA9, .. " */ p { }"u8]);

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("One of the guide's style sheets can't be read.", error.Message);
    }

    [Fact]
    public async Task ExclusivelyLockedHtmlIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        string entry = files.Write("locked.html", "<p>Route</p>");
        using FileStream holder = new(entry, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Rejected(entry);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.StartsWith("locked.html can't be opened.", error.Message);
    }

    [Fact]
    public async Task ShortPdfHasPagesAndText()
    {
        PdfImportManifest manifest = await Manifest<PdfImportManifest>(P0Fixtures.Resolve("pdf-short.pdf"));

        Assert.Equal(2, manifest.PageCount);
        Assert.True(manifest.HasText);
        Assert.Equal("pdf-short", manifest.SuggestedTitle);
    }

    // A scanned PDF has no text; a copy-restricted one is still accepted with its text.
    [Theory]
    [InlineData("pdf-scan.pdf", false)]
    [InlineData("pdf-access.pdf", true)]
    public async Task OnePagePdfReportsWhetherItHasText(string file, bool hasText)
    {
        PdfImportManifest manifest = await Manifest<PdfImportManifest>(P0Fixtures.Resolve(file));

        Assert.Equal(1, manifest.PageCount);
        Assert.Equal(hasText, manifest.HasText);
    }

    private const string WrongAttempt = "wrong-7Q2x";

    [Fact]
    public async Task PasswordProtectedPdfNeedsAPassword()
    {
        string path = P0Fixtures.Resolve("pdf-locked.pdf");

        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(await Inspect(path));

        Assert.Equal("pdf-locked", needs.SuggestedTitle);
        Assert.Equal(new FileInfo(path).Length, needs.Source.ByteCount);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), needs.Fingerprint);
    }

    [Fact]
    public async Task TheRightPasswordGivesAProtectedManifest()
    {
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(P0Fixtures.Resolve("pdf-locked.pdf"), CancellationToken.None));

        PdfImportManifest manifest = await validator.ResolvePdfPasswordAsync(needs, "guide", CancellationToken.None);

        Assert.True(manifest.PasswordRequired);
        Assert.Equal(1, manifest.PageCount);
        Assert.True(manifest.HasText);
        Assert.Equal(needs.Fingerprint, manifest.Fingerprint);
        Assert.Equal(needs.SuggestedTitle, manifest.SuggestedTitle);
    }

    [Fact]
    public async Task WrongPasswordLeavesNoTraceOfTheAttempt()
    {
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(P0Fixtures.Resolve("pdf-locked.pdf"), CancellationToken.None));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => validator.ResolvePdfPasswordAsync(needs, WrongAttempt, CancellationToken.None));

        Assert.Equal(ImportIssue.PasswordIncorrect, error.Issue);
        Assert.Equal("That password didn't open this PDF. Try again.", error.Message);
        Assert.DoesNotContain(WrongAttempt, error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task ChangedSourceAfterInspectionIsChanged()
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-locked.pdf", "locked.pdf");
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(path, CancellationToken.None));
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(-5));

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => validator.ResolvePdfPasswordAsync(needs, "guide", CancellationToken.None));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        Assert.Equal("locked.pdf changed after it was checked. Choose it again.", error.Message);
    }

    [Fact]
    public async Task ReplacedBytesWithTheSameSizeAndTimeAreChanged()
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-locked.pdf", "locked.pdf");
        GuideImportValidator validator = new();
        ImportNeedsPdfPassword needs = Assert.IsType<ImportNeedsPdfPassword>(
            await validator.InspectAsync(path, CancellationToken.None));
        DateTime written = File.GetLastWriteTimeUtc(path);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^2] ^= 0x01;   // inside the trailing whitespace or %%EOF
        File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, written);

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(
            () => validator.ResolvePdfPasswordAsync(needs, "guide", CancellationToken.None));

        Assert.Equal(ImportIssue.Changed, error.Issue);
    }

    [Fact]
    public async Task TextRenamedToPdfIsUnreadable()
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Copy("txt-ascii.txt", "notes.pdf"));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("This file isn't a readable PDF.", error.Message);
    }

    [Fact]
    public async Task TruncatedPdfIsUnreadable()
    {
        using ImportTestDirectory files = new();
        byte[] start = File.ReadAllBytes(P0Fixtures.Resolve("pdf-short.pdf"))[..400];

        GuideImportException error = await Rejected(files.Write("cut.pdf", start));

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.Equal("This file isn't a readable PDF.", error.Message);
    }

    [Fact]
    public async Task UpperCasePdfExtensionIsAccepted()
    {
        using ImportTestDirectory files = new();

        PdfImportManifest manifest = await Manifest<PdfImportManifest>(
            files.Copy("pdf-short.pdf", "MAP.PDF"));

        Assert.Equal("MAP", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task PdfAtTheLimitPassesAndOneByteMoreIsTooLarge()
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-short.pdf", "map.pdf");
        long size = new FileInfo(path).Length;

        await Manifest<PdfImportManifest>(path, new GuideImportLimits(MaxPdfBytes: size));
        GuideImportException error = await Rejected(path, new GuideImportLimits(MaxPdfBytes: size - 1));

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.StartsWith("map.pdf is larger than the ", error.Message);
        Assert.EndsWith(" limit for PDFs.", error.Message);
    }

    [Fact]
    public async Task ExclusivelyLockedPdfIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        string path = files.Copy("pdf-short.pdf", "locked.pdf");
        using FileStream holder = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Rejected(path);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.StartsWith("locked.pdf can't be opened.", error.Message);
    }

    [Fact]
    public void CancellingStopsPdfReadingBeforeTheDocumentIsParsed()
    {
        // Cancel as the header check rewinds, so PdfPig starts parsing
        // with a cancelled token. It ignores the token itself, so only the
        // stream can stop it.
        string path = P0Fixtures.Resolve("pdf-short.pdf");
        using CancellationTokenSource cancel = new();
        using CancelOnRewindStream stream = new(File.ReadAllBytes(path), cancel);
        ImportSource source = new(path, "pdf-short.pdf", stream.Length, DateTimeOffset.UnixEpoch);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            GuideImportValidator.ReadPdf(stream, source, "pdf-short", "", cancel.Token));
        Assert.Equal(0, stream.ReadsAfterCancel);
    }

    private sealed class CancelOnRewindStream(byte[] bytes, CancellationTokenSource cancel)
        : MemoryStream(bytes, writable: false)
    {
        public int ReadsAfterCancel { get; private set; }

        public override long Position
        {
            get => base.Position;
            set
            {
                if (value == 0 && base.Position > 0)
                {
                    cancel.Cancel();
                }
                base.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (cancel.IsCancellationRequested) ReadsAfterCancel++;
            return base.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            if (cancel.IsCancellationRequested) ReadsAfterCancel++;
            return base.Read(buffer);
        }

        public override int ReadByte()
        {
            if (cancel.IsCancellationRequested) ReadsAfterCancel++;
            return base.ReadByte();
        }
    }

    [Fact]
    public async Task InspectingLeavesHtmlAndPdfFixturesUnchanged()
    {
        if (!OperatingSystem.IsWindows()) return;

        string[] sources = ["html-static", "html-hostile", "pdf-short.pdf", "pdf-scan.pdf", "pdf-access.pdf", "pdf-locked.pdf"];
        string[] picked = ["html-static/guide.html", "html-hostile/guide.html", "pdf-short.pdf", "pdf-scan.pdf", "pdf-access.pdf", "pdf-locked.pdf"];
        var before = sources.SelectMany(s => FileFingerprint.Of(P0Fixtures.Resolve(s))).ToArray();
        GuideImportValidator validator = new();

        foreach (string path in picked)
        {
            try
            {
                await validator.InspectAsync(P0Fixtures.Resolve(path), CancellationToken.None);
            }
            catch (GuideImportException)
            {
            }
        }

        Assert.Equal(before, sources.SelectMany(s => FileFingerprint.Of(P0Fixtures.Resolve(s))).ToArray());
    }

    [Fact]
    public async Task PdfManifestCarriesTheFileHash()
    {
        PdfImportManifest pdf = await Manifest<PdfImportManifest>(P0Fixtures.Resolve("pdf-short.pdf"));

        Assert.Equal("b67dd6f52454ead4b99571b5a66e8be6a63c3a34a522db5b4670d57cc0f0006f", pdf.Fingerprint);
    }

    [Fact]
    public async Task TypedIssuesStayDistinct()
    {
        using ImportTestDirectory files = new();
        GuideImportException[] errors =
        [
            await Rejected(Path.Combine(files.Root, "gone.txt")),
            await Rejected(files.Write("notes.doc", "text")),
            await Rejected(files.Write("empty.txt", "")),
            await Rejected(files.Copy("txt-ascii.txt", "notes.pdf")),
        ];

        Assert.Equal(
            [ImportIssue.Missing, ImportIssue.Unsupported, ImportIssue.Empty, ImportIssue.Unreadable],
            errors.Select(error => error.Issue));
        Assert.Equal(errors.Length, errors.Select(error => error.Message).Distinct().Count());
    }
}
