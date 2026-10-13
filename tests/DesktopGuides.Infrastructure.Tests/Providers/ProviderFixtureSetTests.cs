using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Artwork;
using DesktopGuides.Infrastructure.Providers;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests.Providers;

// T17.2 D: the fixture set CI's fake-provider lane installs, checked against
// the real provider clients so a broken fixture fails here, not in an
// installed run.
public sealed class ProviderFixtureSetTests : IDisposable
{
    private static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "ProviderFixtures");
    private static readonly IgdbCredentials Fake = new("desktop-guides-fixture-client", "desktop-guides-fixture-secret");
    private const string FakeSteamGridDbKey = "desktop-guides-fixture-steamgriddb-key";
    private readonly string scratch = Path.Combine(Path.GetTempPath(), "dg-provider-set-" + Guid.NewGuid().ToString("N"));

    // The installer copies every top-level file but credentials/.
    public ProviderFixtureSetTests()
    {
        Directory.CreateDirectory(scratch);
        foreach (string file in Directory.GetFiles(Shipped))
        {
            File.Copy(file, Path.Combine(scratch, Path.GetFileName(file)));
        }
    }

    public void Dispose() => Directory.Delete(scratch, recursive: true);

    [Fact]
    public async Task TheFixturesDriveTheRealProviderClients()
    {
        using ProviderHttp http = new(new FixtureProviderHandler(scratch));
        using TwitchTokenSource tokens = new(http);
        IgdbClient igdb = new(http, tokens, _ => Task.FromResult<IgdbCredentials?>(Fake));

        await igdb.TestConnectionAsync(CancellationToken.None);
        IReadOnlyList<ProviderSearchResult> results = await igdb.SearchAsync("Half-Life", CancellationToken.None);
        ProviderSearchResult halfLife = Assert.Single(results, r => r.Title == "Half-Life" && r.ReleaseYear == 1998);
        Assert.Empty(await igdb.SearchAsync("zzqx no such game 91377", CancellationToken.None));
        Assert.NotEmpty(await igdb.SearchAsync("Final Fantasy", CancellationToken.None));

        ProviderGameRecord record = await igdb.GetAsync(halfLife.ExternalId, CancellationToken.None);
        Assert.Equal("Half-Life", record.Title);
        Assert.Equal("70", record.Hints.SteamAppId);

        SteamGridDbArtworkSource grids = new(http, _ => Task.FromResult<string?>(FakeSteamGridDbKey));
        await grids.TestConnectionAsync(CancellationToken.None);
        ArtworkCandidate grid = Assert.IsType<ArtworkCandidate>(await grids.FindAsync(record.Hints, CancellationToken.None));
        ArtworkCandidate cover = Assert.IsType<ArtworkCandidate>(
            await new IgdbCoverArtworkSource().FindAsync(record.Hints, CancellationToken.None));
        foreach (Uri image in new[] { grid.Url, cover.Url, new Uri(halfLife.ThumbnailUrl!) })
        {
            ArtworkValidator.Validate(await http.GetImageAsync(image, CancellationToken.None));
        }

        string unmatched = Path.Combine(scratch, FixtureProviderHandler.UnmatchedName);
        Assert.False(File.Exists(unmatched), File.Exists(unmatched) ? File.ReadAllText(unmatched) : "");
    }

    [Fact]
    public async Task TheSmokesWrongSecretIsRejected()
    {
        using ProviderHttp http = new(new FixtureProviderHandler(scratch));
        using TwitchTokenSource tokens = new(http);

        ProviderException rejected = await Assert.ThrowsAsync<ProviderException>(() => tokens.GetTokenAsync(
            Fake with { ClientSecret = "desktop-guides-wrong-secret" }, forceRefresh: true, CancellationToken.None));
        Assert.Equal(ProviderErrorKind.InvalidCredentials, rejected.Kind);
    }

    // Review Focus 3: the installer's leak scan searches the app's data folder,
    // where these files are copied, for the fake credential values.
    [Fact]
    public void NoCopiedFixtureHoldsAFakeCredentialValue()
    {
        string[] values = [Fake.ClientId, Fake.ClientSecret, FakeSteamGridDbKey];
        foreach (string file in Directory.GetFiles(scratch))
        {
            string text = File.ReadAllText(file);
            foreach (string value in values) Assert.DoesNotContain(value, text);
        }
        Assert.True(File.Exists(Path.Combine(Shipped, "credentials", "igdb-credentials.txt")));
    }
}
