using DesktopGuides.Core.Providers;
using Xunit;

namespace DesktopGuides.Core.Tests.Providers;

public sealed class FallbackArtworkSourceTests
{
    private static readonly ArtworkHints Hints = new("Half-Life", "70", "co1abc");

    private sealed class Source(Func<ArtworkCandidate?> find) : IArtworkSource
    {
        public int Calls { get; private set; }
        public Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token)
        {
            Calls++;
            token.ThrowIfCancellationRequested();
            return Task.FromResult(find());
        }
    }

    private static ArtworkCandidate Candidate(string name) => new(new Uri($"https://{name}.test/a.png"), name);

    [Fact]
    public async Task YieldsCandidatesInOrderAndSkipsEmptyAndFailingSources()
    {
        Source failing = new(() => throw new ProviderException(ProviderErrorKind.InvalidCredentials, "no"));
        Source empty = new(() => null);
        Source igdb = new(() => Candidate("IGDB"));
        FallbackArtworkSource chain = new(failing, empty, igdb);

        List<ArtworkCandidate> found = [];
        await foreach (ArtworkCandidate candidate in chain.FindCandidatesAsync(Hints, default)) found.Add(candidate);

        Assert.Equal([Candidate("IGDB")], found);
        Assert.Equal((1, 1, 1), (failing.Calls, empty.Calls, igdb.Calls));
    }

    [Fact]
    public async Task StopsAtTheFirstCandidateTheCallerAccepts()
    {
        Source first = new(() => Candidate("SteamGridDB"));
        Source second = new(() => Candidate("IGDB"));

        await foreach (ArtworkCandidate _ in new FallbackArtworkSource(first, second).FindCandidatesAsync(Hints, default))
        {
            break;
        }
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task CancellationIsNotSwallowed()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (ArtworkCandidate _ in new FallbackArtworkSource(new Source(() => null))
                               .FindCandidatesAsync(Hints, cancel.Token)) { }
        });
    }
}
