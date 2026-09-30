using System.Text;
using DesktopGuides.Core.Import;
using DesktopGuides.Core.Library;
using DesktopGuides.Infrastructure.Import;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Import;

public sealed class GuideImportValidatorTests
{
    private const string NotUtf8 = "This text file isn't UTF-8. Save it as UTF-8 and import it again.";

    private static Task<ImportInspection> Inspect(string path, GuideImportLimits? limits = null) =>
        new GuideImportValidator(limits).InspectAsync(path, CancellationToken.None);

    private static async Task<GuideImportException> Rejected(string path, GuideImportLimits? limits = null) =>
        await Assert.ThrowsAsync<GuideImportException>(() => Inspect(path, limits));

    [Theory]
    [InlineData("txt-ascii.txt")]
    [InlineData("txt-utf8.txt")]
    [InlineData("txt-bom.txt")]
    public async Task Utf8TextFixturesAreReady(string fixture)
    {
        string path = P0Fixtures.Resolve(fixture);

        ImportReady ready = Assert.IsType<ImportReady>(await Inspect(path));

        TxtImportManifest manifest = Assert.IsType<TxtImportManifest>(ready.Manifest);
        Assert.Null(manifest.CodePage);
        Assert.Equal(GuideFormat.Txt, manifest.Format);
        Assert.Equal(Path.GetFileNameWithoutExtension(fixture), manifest.SuggestedTitle);
        Assert.Equal(fixture, manifest.Source.FileName);
        Assert.Equal(new FileInfo(path).Length, manifest.Source.ByteCount);
        Assert.Equal(Path.GetFullPath(path), manifest.Source.FullPath);
    }

    [Fact]
    public async Task LegacyTextNeedsAnEncodingWithBothSamples()
    {
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await Inspect(P0Fixtures.Resolve("txt-legacy.txt")));

        Assert.Equal("txt-legacy", needs.SuggestedTitle);
        Assert.Equal([437, 1252], needs.Samples.Select(sample => sample.CodePage));
        Assert.Equal("Guide é\nItem list", needs.Samples[0].Text);
        Assert.Equal("Guide ‚\nItem list", needs.Samples[1].Text);
    }

    [Theory]
    [InlineData(437)]
    [InlineData(1252)]
    public async Task ResolveStoresTheChosenCodePage(int codePage)
    {
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(P0Fixtures.Resolve("txt-legacy.txt"), CancellationToken.None));

        TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(
            needs, codePage, CancellationToken.None);

        Assert.Equal(codePage, manifest.CodePage);
        Assert.Equal(needs.Source, manifest.Source);
        Assert.Equal("txt-legacy", manifest.SuggestedTitle);
    }

    [Fact]
    public async Task ResolveRejectsOtherCodePages()
    {
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(P0Fixtures.Resolve("txt-legacy.txt"), CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            validator.ResolveTxtEncodingAsync(needs, 65001, CancellationToken.None));
    }

    [Fact]
    public async Task Windows1252ResolvesBytesItLeavesUndefined()
    {
        using ImportTestDirectory files = new();
        string path = files.Write("map.txt", [0x4D, 0x61, 0x70, 0x20, 0x81, 0x8D, 0x8F, 0x90, 0x9D]);
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(path, CancellationToken.None));

        TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(
            needs, 1252, CancellationToken.None);

        Assert.Equal(1252, manifest.CodePage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolveReportsChangedWhenTheFileChanged(bool grow)
    {
        using ImportTestDirectory files = new();
        string path = files.Copy("txt-legacy.txt", "legacy.txt");
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(path, CancellationToken.None));
        if (grow)
        {
            File.AppendAllText(path, "more");
            File.SetLastWriteTimeUtc(path, needs.Source.LastWriteUtc.UtcDateTime);
        }
        else
        {
            File.SetLastWriteTimeUtc(path, needs.Source.LastWriteUtc.UtcDateTime.AddMinutes(1));
        }

        GuideImportException error = await Assert.ThrowsAsync<GuideImportException>(() =>
            validator.ResolveTxtEncodingAsync(needs, 437, CancellationToken.None));

        Assert.Equal(ImportIssue.Changed, error.Issue);
        Assert.Equal("legacy.txt changed after it was checked. Choose it again.", error.Message);
    }

    public static TheoryData<byte[]> NotUtf8Bytes => new()
    {
        { new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x42, 0x00 } },
        { new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x00, 0x42 } },
        { Encoding.ASCII.GetBytes("abc\0def") },
    };

    [Theory]
    [MemberData(nameof(NotUtf8Bytes))]
    public async Task Utf16AndNulAreAnUnsupportedEncoding(byte[] bytes)
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Write("wide.txt", bytes));

        Assert.Equal(ImportIssue.UnsupportedEncoding, error.Issue);
        Assert.Equal(NotUtf8, error.Message);
    }

    [Fact]
    public async Task SamplesStayWithinEightLinesAndTwoKilobytes()
    {
        using ImportTestDirectory files = new();
        byte[] line = [.. Encoding.ASCII.GetBytes("Room "), 0x82, .. Encoding.ASCII.GetBytes(new string('.', 90)), 0x0D, 0x0A];
        byte[] bytes = Enumerable.Repeat(line, 3 * 1024 * 1024 / line.Length).SelectMany(part => part).ToArray();

        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await Inspect(files.Write("long legacy.txt", bytes)));

        Assert.All(needs.Samples, sample =>
        {
            Assert.True(sample.Text.Split('\n').Length <= 8);
            Assert.True(sample.Text.Length <= 2048);
            Assert.DoesNotContain('\r', sample.Text);
        });
    }

    [Fact]
    public async Task TextWorkRunsOffTheCallersThread()
    {
        // The dialog calls in on the UI thread; decoding a large file there
        // would freeze Cancel. A cached read can complete synchronously, so
        // the call must return before the work is done and never post back.
        using ImportTestDirectory files = new();
        string path = files.Write("big.txt", Encoding.UTF8.GetBytes(new string('a', 4 * 1024 * 1024)));
        GuideImportValidator validator = new();
        PostCountingContext context = new();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task<ImportInspection> inspecting;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            inspecting = validator.InspectAsync(path, CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.False(inspecting.IsCompleted);
        Assert.IsType<ImportReady>(await inspecting);
        Assert.Equal(0, context.Posts);
    }

    private sealed class PostCountingContext : SynchronizationContext
    {
        private int posts;

        public int Posts => Volatile.Read(ref posts);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref posts);
            base.Post(callback, state);
        }
    }

    [Theory]
    [InlineData("WALKTHRU.TXT")]
    [InlineData("Walkthru.Txt")]
    public async Task UpperCaseExtensionsAreAccepted(string name)
    {
        using ImportTestDirectory files = new();

        ImportReady ready = Assert.IsType<ImportReady>(
            await Inspect(files.Copy("txt-ascii.txt", name)));

        Assert.Equal("WALKTHRU", ready.Manifest.SuggestedTitle.ToUpperInvariant());
    }

    [Theory]
    [InlineData("guide.docx")]
    [InlineData("guide.txt.exe")]
    [InlineData("README")]
    public async Task OtherExtensionsAreUnsupported(string name)
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Write(name, "text"));

        Assert.Equal(ImportIssue.Unsupported, error.Issue);
        Assert.Equal($"{name} isn't a TXT, HTML or PDF file.", error.Message);
    }

    [Fact]
    public async Task MissingPathIsMissing()
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(Path.Combine(files.Root, "gone.txt"));

        Assert.Equal(ImportIssue.Missing, error.Issue);
        Assert.Equal("gone.txt can't be found. It may have been moved or deleted.", error.Message);
    }

    [Fact]
    public async Task RelativePathIsAnArgumentError() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Inspect("guide.txt"));

    [Theory]
    [InlineData("empty.txt")]
    [InlineData("empty.html")]
    [InlineData("empty.pdf")]
    public async Task ZeroByteFilesAreEmpty(string name)
    {
        using ImportTestDirectory files = new();

        GuideImportException error = await Rejected(files.Write(name, []));

        Assert.Equal(ImportIssue.Empty, error.Issue);
        Assert.Equal("The file is empty.", error.Message);
    }

    [Fact]
    public async Task ExclusivelyLockedTextIsUnreadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        using ImportTestDirectory files = new();
        string path = files.Copy("txt-ascii.txt", "locked.txt");
        using FileStream holder = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        GuideImportException error = await Rejected(path);

        Assert.Equal(ImportIssue.Unreadable, error.Issue);
        Assert.StartsWith("locked.txt can't be opened.", error.Message);
    }

    [Theory]
    [InlineData("txt-ascii.txt")]
    [InlineData("html-static/guide.html")]
    [InlineData("pdf-short.pdf")]
    public async Task ACancelledTokenThrows(string fixture)
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GuideImportValidator().InspectAsync(P0Fixtures.Resolve(fixture), cancel.Token));
    }

    [Fact]
    public async Task TextAtTheLimitPassesAndOneByteMoreIsTooLarge()
    {
        using ImportTestDirectory files = new();
        GuideImportLimits limits = new(MaxTxtBytes: 20);
        string atLimit = files.Write("at.txt", new string('a', 20));
        string over = files.Write("over.txt", new string('a', 21));

        Assert.IsType<ImportReady>(await Inspect(atLimit, limits));
        GuideImportException error = await Rejected(over, limits);

        Assert.Equal(ImportIssue.TooLarge, error.Issue);
        Assert.Equal("over.txt is larger than the 20 bytes limit for text files.", error.Message);
    }

    [Fact]
    public async Task InspectingAndResolvingLeaveTextFixturesUnchanged()
    {
        string[] fixtures = ["txt-ascii.txt", "txt-utf8.txt", "txt-bom.txt", "txt-legacy.txt"];
        var before = fixtures.Select(f => FileFingerprint.Of(P0Fixtures.Resolve(f))).ToArray();
        GuideImportValidator validator = new();

        foreach (string fixture in fixtures)
        {
            if (await validator.InspectAsync(P0Fixtures.Resolve(fixture), CancellationToken.None)
                is ImportNeedsTxtEncoding needs)
            {
                await validator.ResolveTxtEncodingAsync(needs, 437, CancellationToken.None);
            }
        }

        var after = fixtures.Select(f => FileFingerprint.Of(P0Fixtures.Resolve(f))).ToArray();
        Assert.Equal(before.SelectMany(x => x), after.SelectMany(x => x));
    }

    [Fact]
    public async Task Utf8TextManifestCarriesTheFileHash()
    {
        ImportReady ready = Assert.IsType<ImportReady>(await Inspect(P0Fixtures.Resolve("txt-utf8.txt")));

        Assert.Equal("e21e7137eca4d996fced2143d7111220ea5051dc40708103cc6b7c490354b779", ready.Manifest.Fingerprint);
    }

    [Fact]
    public async Task ResolvedTextManifestCarriesTheFileHash()
    {
        GuideImportValidator validator = new();
        ImportNeedsTxtEncoding needs = Assert.IsType<ImportNeedsTxtEncoding>(
            await validator.InspectAsync(P0Fixtures.Resolve("txt-legacy.txt"), CancellationToken.None));

        TxtImportManifest manifest = await validator.ResolveTxtEncodingAsync(needs, 437, CancellationToken.None);

        Assert.Equal("f105c9c5952018b15edc617ecced30a4e2bccd3e8fa252ce7792f1a1ac723d26", manifest.Fingerprint);
    }
}
