using DesktopGuides.Infrastructure.Online;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Online;

public sealed class OnlineFixtureRoutesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "dg-routes-" + Guid.NewGuid().ToString("N"));

    public OnlineFixtureRoutesTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "gamefaqs"));
        File.WriteAllText(Path.Combine(root, "gamefaqs", "page.html"), "<p>x</p>");
        Write(200);
    }

    public void Dispose() => Directory.Delete(root, true);

    private void Write(int status) => File.WriteAllText(Path.Combine(root, "manifest.json"), $$"""
        { "routes": [ { "url": "https://gamefaqs.gamespot.com/x/1-y/faqs/2", "status": {{status}}, "file": "gamefaqs/page.html", "contentType": "text/html" } ] }
        """);

    [Fact]
    public void FixtureRoutesAnswerKnownUrlsAnd599Otherwise()
    {
        OnlineFixtureRoutes routes = OnlineFixtureRoutes.Load(Path.Combine(root, "manifest.json"));

        Assert.True(routes.TryAnswer(new Uri("https://gamefaqs.gamespot.com/x/1-y/faqs/2"), out int status, out string type, out string? file));
        Assert.Equal((200, "text/html", Path.Combine(root, "gamefaqs", "page.html")), (status, type, file));
        Assert.False(routes.TryAnswer(new Uri("https://gamefaqs.gamespot.com/other"), out status, out _, out file));
        Assert.Equal(599, status);
        Assert.Null(file);
    }

    [Fact]
    public void ARewrittenManifestIsReadAgain()
    {
        OnlineFixtureRoutes routes = OnlineFixtureRoutes.Load(Path.Combine(root, "manifest.json"));
        Write(429);
        File.SetLastWriteTimeUtc(Path.Combine(root, "manifest.json"), DateTime.UtcNow.AddSeconds(5));
        routes.TryAnswer(new Uri("https://gamefaqs.gamespot.com/x/1-y/faqs/2"), out int status, out _, out _);
        Assert.Equal(429, status);
    }

    [Fact]
    public void AFileOutsideTheManifestsFolderIsRefused()
    {
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            { "routes": [ { "url": "https://gamefaqs.gamespot.com/a", "status": 200, "file": "../secret.txt", "contentType": "text/plain" } ] }
            """);
        Assert.Throws<InvalidDataException>(() => OnlineFixtureRoutes.Load(Path.Combine(root, "manifest.json")));
    }

    // The installed online group's manifest loads, and every route's file exists.
    [Fact]
    public void TheInstalledGroupsManifestIsValid()
    {
        string manifest = Path.GetFullPath(Path.Combine(
            DesktopGuides.Infrastructure.Tests.Import.P0Fixtures.Root, "..", "p3", "manifest.json"));
        OnlineFixtureRoutes routes = OnlineFixtureRoutes.Load(manifest);
        foreach (string url in new[]
        {
            "https://gamefaqs.gamespot.com/search?game=Online%20Test%20Game",
            "https://gamefaqs.gamespot.com/ps5/100002-example-saga/faqs/78736/cairn",
            "https://gamefaqs.gamespot.com/a/faqs/36/78736-54.png",
        })
        {
            Assert.True(routes.TryAnswer(new Uri(url), out int status, out _, out string? file), url);
            Assert.Equal(200, status);
            Assert.True(File.Exists(file), file);
        }
    }
}
