using System.Runtime.CompilerServices;

namespace DesktopGuides.Core.Providers;

public sealed class FallbackArtworkSource(params IArtworkSource[] sources)
{
    public async IAsyncEnumerable<ArtworkCandidate> FindCandidatesAsync(
        ArtworkHints hints, [EnumeratorCancellation] CancellationToken token)
    {
        foreach (IArtworkSource source in sources)
        {
            token.ThrowIfCancellationRequested();
            ArtworkCandidate? candidate;
            try
            {
                candidate = await source.FindAsync(hints, token);
            }
            catch (ProviderException)
            {
                continue;
            }
            if (candidate is not null) yield return candidate;
        }
    }
}
