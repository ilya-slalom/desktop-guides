using System.Security.Cryptography;
using DesktopGuides.Core.Library;
using DesktopGuides.Core.Providers;

namespace DesktopGuides.Core.Tests.Providers;

internal sealed class FakeRepository : ILibraryRepository
{
    public Dictionary<Guid, Game> Games { get; } = [];
    public Guid? RaceWinner { get; set; }
    public bool FailUpdates { get; set; }
    public int FindCalls { get; private set; }

    public Task<Game?> FindLinkedGameAsync(string provider, string externalId, CancellationToken token = default)
    {
        FindCalls++;
        return Task.FromResult(Games.Values.FirstOrDefault(g =>
            g.Link?.Provider == provider && g.Link.ExternalId == externalId));
    }

    public Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (RaceWinner is { } winner) throw new DuplicateProviderLinkException(winner);
        Game added = new(game.Id, game.Title, game.Platform, null, game.Link.RetrievedUtc,
            game.Link.RetrievedUtc, game.Link, game.Metadata, game.ArtworkRelativePath);
        Games.Add(added.Id, added);
        return Task.FromResult(added);
    }

    public Task<Game> UpdateGameMetadataAsync(Guid gameId, GameMetadataSnapshot metadata,
        DateTimeOffset retrievedUtc, string? artworkRelativePath, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (FailUpdates) throw new InvalidOperationException("update failed");
        Game current = Games[gameId];
        Game updated = current with
        {
            Link = current.Link! with { RetrievedUtc = retrievedUtc },
            Metadata = metadata,
            ArtworkRelativePath = artworkRelativePath,
            UpdatedUtc = retrievedUtc,
        };
        Games[gameId] = updated;
        return Task.FromResult(updated);
    }

    public Task<Game?> GetGameAsync(Guid gameId, CancellationToken token = default) =>
        Task.FromResult(Games.GetValueOrDefault(gameId));

    public StartupReconciliationReport? LastStartupReconciliation => null;
    public Task InitializeAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task<Game> AddGameAsync(string title, string? platform, string? notes, CancellationToken token = default) => throw new NotSupportedException();
    public Task<Game> UpdateGameAsync(Guid gameId, string title, string? platform, string? notes, CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guide>> ListGuidesAsync(Guid gameId, CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(Guid gameId, CancellationToken token = default) => throw new NotSupportedException();
    public Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ReadingState?> GetReadingStateAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task SaveReadingLocationAsync(Guid guideId, string locatorJson, double? estimatedFraction, CancellationToken token = default) => throw new NotSupportedException();
    public Task RecordGuideOpenedAsync(Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default) => throw new NotSupportedException();
    public Task<DateTimeOffset?> SetGuideCompletionAsync(Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default) => throw new NotSupportedException();
    public Task<ReaderPreferences?> GetReaderPreferencesAsync(Guid guideId, CancellationToken token = default) => throw new NotSupportedException();
    public Task SaveReaderPreferencesAsync(Guid guideId, double? textScale, CancellationToken token = default) => throw new NotSupportedException();
    public Task<AppSettings> GetSettingsAsync(CancellationToken token = default) => throw new NotSupportedException();
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default) => throw new NotSupportedException();
    public Task<AppSettings> UpdateSettingsAsync(Func<AppSettings, AppSettings> update, CancellationToken token = default) => throw new NotSupportedException();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class FakeStore : IArtworkStore
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public List<string> Deleted { get; } = [];
    public Action? OnStored { get; set; }

    public Task<StoredArtwork> StoreAsync(Guid gameId, byte[] content, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (content.Length == 0) throw new InvalidDataException("not an image");
        string sha = Convert.ToHexStringLower(SHA256.HashData(content));
        string path = $"artwork/{gameId:N}/{sha}.png";
        Files[path] = content;
        OnStored?.Invoke();
        return Task.FromResult(new StoredArtwork(path, sha));
    }

    public Exception? DeleteFailure { get; set; }

    public void Delete(string relativePath)
    {
        if (DeleteFailure is not null) throw DeleteFailure;
        Deleted.Add(relativePath);
        Files.Remove(relativePath);
    }

    public string? ResolveFile(string relativePath) => Files.ContainsKey(relativePath) ? relativePath : null;
}

internal sealed class FakeProvider(params ProviderGameRecord[] records) : IGameMetadataProvider
{
    public ProviderException? Failure { get; set; }
    public int GetCalls { get; private set; }

    public Task<IReadOnlyList<ProviderSearchResult>> SearchAsync(string query, CancellationToken token) =>
        throw new NotSupportedException();

    public Task<ProviderGameRecord> GetAsync(string externalId, CancellationToken token)
    {
        GetCalls++;
        token.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        return Task.FromResult(records.Single(r => r.ExternalId == externalId));
    }
}

internal sealed class FixedSource(ArtworkCandidate? candidate) : IArtworkSource
{
    public Task<ArtworkCandidate?> FindAsync(ArtworkHints hints, CancellationToken token) => Task.FromResult(candidate);
}
