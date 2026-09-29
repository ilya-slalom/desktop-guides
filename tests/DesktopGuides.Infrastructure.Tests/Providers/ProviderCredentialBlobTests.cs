using System.Text;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

public sealed class ProviderCredentialBlobTests
{
    private static readonly ProviderCredentials Full = new(new IgdbCredentials("abc123", "s3cret-value"), "sgdb-key-1");

    [Fact]
    public void RoundTripsEveryField()
    {
        Assert.Equal(Full, ProviderCredentialBlob.Parse(ProviderCredentialBlob.Format(Full)));
        ProviderCredentials igdbOnly = Full with { SteamGridDbKey = null };
        Assert.Equal(igdbOnly, ProviderCredentialBlob.Parse(ProviderCredentialBlob.Format(igdbOnly)));
        Assert.Equal(ProviderCredentials.None,
            ProviderCredentialBlob.Parse(ProviderCredentialBlob.Format(ProviderCredentials.None)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"v":2,"igdb":{"clientId":"a","clientSecret":"b"}}""")]
    [InlineData("""{"v":1,"igdb":{"clientId":"a"}}""")]
    [InlineData("""{"v":1,"igdb":{"clientId":"a b","clientSecret":"b"}}""")]
    [InlineData("""{"v":1,"steamGridDbKey":7}""")]
    public void UnreadableBlobIsNotConfigured(string json) =>
        Assert.Equal(ProviderCredentials.None, ProviderCredentialBlob.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void OversizedBlobIsNotConfigured()
    {
        byte[] big = Encoding.UTF8.GetBytes($$"""{"v":1,"steamGridDbKey":"{{new string('k', ProviderCredentialBlob.MaxBytes)}}"}""");
        Assert.Equal(ProviderCredentials.None, ProviderCredentialBlob.Parse(big));
    }

    [Fact]
    public void NormalizeTrimsAndTreatsBlankAsUnset()
    {
        Assert.Equal(Full, ProviderCredentialBlob.Normalize(" abc123 ", "s3cret-value\t", "sgdb-key-1"));
        Assert.Equal(ProviderCredentials.None, ProviderCredentialBlob.Normalize("", "  ", null));
    }

    [Theory]
    [InlineData("abc123", null, null, "IGDB client secret")]
    [InlineData(null, "s3cret-value", null, "IGDB client ID")]
    [InlineData("abc 123", "s3cret-value", null, "IGDB client ID")]
    [InlineData("abc123", "s3creté", null, "IGDB client secret")]
    [InlineData(null, null, "key\u0001", "SteamGridDB API key")]
    public void NormalizeNamesTheBadFieldWithoutEchoingIt(string? id, string? secret, string? key, string field)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize(id, secret, key));
        Assert.Contains(field, error.Message);
        foreach (string? value in new[] { id, secret, key })
        {
            if (value is { Length: > 3 }) Assert.DoesNotContain(value.Trim(), error.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NormalizeEnforcesLengthLimits()
    {
        Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize(new string('a', 65), "s", null));
        Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize("a", new string('s', 129), null));
        Assert.Throws<ArgumentException>(() => ProviderCredentialBlob.Normalize(null, null, new string('k', 129)));
        Assert.NotNull(ProviderCredentialBlob.Normalize(new string('a', 64), new string('s', 128), new string('k', 128)).Igdb);
    }
}
