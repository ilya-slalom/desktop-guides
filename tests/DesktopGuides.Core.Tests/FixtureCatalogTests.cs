using System.Security.Cryptography;
using System.Text;
using DesktopGuides.Core.Fixtures;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class FixtureCatalogTests
{
    [Fact]
    public void LoadsAndVerifiesAFixtureById()
    {
        using FixtureDirectory fixtures = new("sample.txt", "Guide text", "valid");

        FixtureCatalog catalog = FixtureCatalog.Load(fixtures.Root);

        Assert.Single(catalog.Entries);
        Assert.Equal("valid", catalog.Entries[0].Id);
        Assert.Equal(Path.Combine(fixtures.Root, "sample.txt"), catalog.Resolve("valid"));
    }

    [Fact]
    public void RejectsFixturePathOutsideTheRoot()
    {
        using FixtureDirectory fixtures = new("../outside.txt", "Guide text", "escape");

        Assert.Throws<InvalidDataException>(() => FixtureCatalog.Load(fixtures.Root));
    }

    [Fact]
    public void RejectsContentWhoseHashDoesNotMatchTheManifest()
    {
        using FixtureDirectory fixtures = new("sample.txt", "Guide text", "changed");
        File.WriteAllText(Path.Combine(fixtures.Root, "sample.txt"), "Different text");

        Assert.Throws<InvalidDataException>(() => FixtureCatalog.Load(fixtures.Root));
    }

    private sealed class FixtureDirectory : IDisposable
    {
        public FixtureDirectory(string relativePath, string content, string id)
        {
            Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "desktop-guides-test-" + Guid.NewGuid());
            Directory.CreateDirectory(Root);
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            if (relativePath == "sample.txt")
            {
                File.WriteAllBytes(System.IO.Path.Combine(Root, relativePath), bytes);
            }
            string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            File.WriteAllText(System.IO.Path.Combine(Root, "manifest.json"),
                $$"""{"schemaVersion":1,"fixtures":[{"id":"{{id}}","path":"{{relativePath}}","format":"txt","bytes":{{bytes.Length}},"sha256":"{{sha}}","expectations":"test"}]}""");
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, true);
    }
}
