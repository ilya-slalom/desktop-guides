using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Providers;

public sealed record ProviderAddResult(Game Game, bool AlreadyInLibrary, bool ArtworkMissing);

public sealed record ProviderRefreshResult(Game Game, bool ArtworkMissing);

public sealed class ProviderGameImporter(
    ILibraryRepository repository,
    IGameMetadataProvider provider,
    FallbackArtworkSource artwork,
    IArtworkStore store,
    Func<Uri, CancellationToken, Task<byte[]>> download,
    TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    public async Task<ProviderAddResult> AddAsync(string externalId, CancellationToken token)
    {
        if (await repository.FindLinkedGameAsync(ProviderGameLink.Igdb, externalId, token) is { } existing)
        {
            return new ProviderAddResult(existing, AlreadyInLibrary: true, ArtworkMissing: false);
        }
        ProviderGameRecord record = await provider.GetAsync(externalId, token);
        Guid id = Guid.NewGuid();
        (StoredArtwork? stored, string? source) = await StoreArtworkAsync(id, record.Hints, token);
        try
        {
            token.ThrowIfCancellationRequested();
            Game game = await repository.AddLinkedGameAsync(new NewLinkedGame(
                id,
                record.Title,
                record.Snapshot.Platforms is [string only] ? only : null,
                new ProviderGameLink(ProviderGameLink.Igdb, record.ExternalId, clock.GetUtcNow()),
                record.Snapshot with { ArtworkSource = source },
                stored?.RelativePath), token);
            await StoreSteamAppIdAsync(game.Id, record.Hints.SteamAppId);
            return new ProviderAddResult(game, AlreadyInLibrary: false, ArtworkMissing: stored is null);
        }
        catch (DuplicateProviderLinkException duplicate)
        {
            Delete(stored);
            Game winner = await repository.GetGameAsync(duplicate.ExistingGameId, CancellationToken.None)
                ?? throw new InvalidOperationException("The game that holds this provider link was removed.");
            return new ProviderAddResult(winner, AlreadyInLibrary: true, ArtworkMissing: false);
        }
        catch
        {
            Delete(stored);
            throw;
        }
    }

    public async Task<ProviderRefreshResult> RefreshAsync(Guid gameId, CancellationToken token)
    {
        Game current = await repository.GetGameAsync(gameId, token)
            ?? throw new KeyNotFoundException("The game no longer exists.");
        if (current.Link is not { } link)
        {
            throw new InvalidOperationException("Only a game added from search can be refreshed.");
        }
        ProviderGameRecord record = await provider.GetAsync(link.ExternalId, token);
        (StoredArtwork? stored, string? source) = await StoreArtworkAsync(gameId, record.Hints, token);
        StoredArtwork? added = stored?.RelativePath == current.ArtworkRelativePath ? null : stored;
        string? path = stored?.RelativePath ?? current.ArtworkRelativePath;
        GameMetadataSnapshot snapshot = record.Snapshot with
        {
            ArtworkSource = stored is null ? current.Metadata?.ArtworkSource : source,
        };
        Game updated;
        try
        {
            token.ThrowIfCancellationRequested();
            updated = await repository.UpdateGameMetadataAsync(gameId, snapshot, clock.GetUtcNow(), path, token);
        }
        catch
        {
            Delete(added);
            throw;
        }
        if (added is not null && current.ArtworkRelativePath is { } previous) store.Delete(previous);
        await StoreSteamAppIdAsync(gameId, record.Hints.SteamAppId);
        return new ProviderRefreshResult(updated, ArtworkMissing: path is null);
    }

    // S22: Steam guide search needs it. A missing ID keeps any stored one, and
    // a failure to store it doesn't undo the add or refresh: the game page
    // asks for the game's Steam URL instead.
    private async Task StoreSteamAppIdAsync(Guid gameId, string? steamAppId)
    {
        if (steamAppId is null) return;
        try
        {
            await repository.SetSteamAppIdAsync(gameId, steamAppId, CancellationToken.None);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
        }
    }

    private async Task<(StoredArtwork? Stored, string? Source)> StoreArtworkAsync(
        Guid gameId, ArtworkHints hints, CancellationToken token)
    {
        await foreach (ArtworkCandidate candidate in artwork.FindCandidatesAsync(hints, token))
        {
            try
            {
                byte[] content = await download(candidate.Url, token);
                return (await store.StoreAsync(gameId, content, token), candidate.SourceName);
            }
            catch (Exception error) when (error is ProviderException or InvalidDataException)
            {
                // Ruling T9-a: try the next candidate.
            }
        }
        return (null, null);
    }

    private void Delete(StoredArtwork? stored)
    {
        if (stored is not null) store.Delete(stored.RelativePath);
    }
}
