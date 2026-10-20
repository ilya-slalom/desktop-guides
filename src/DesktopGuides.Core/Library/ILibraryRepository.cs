using DesktopGuides.Core.Providers;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Core.Library;

public interface ILibraryRepository : IReadingLocationStore, IGuideCompletionStore, IBookmarkStore, IAsyncDisposable
{
    StartupReconciliationReport? LastStartupReconciliation { get; }
    Task InitializeAsync(CancellationToken token = default);
    Task<Game> AddGameAsync(
        string title, string? platform, string? notes, CancellationToken token = default);
    Task<Game> UpdateGameAsync(
        Guid gameId, string title, string? platform, string? notes,
        CancellationToken token = default);
    Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken token = default);
    Task<Game?> GetGameAsync(Guid gameId, CancellationToken token = default);
    Task<IReadOnlyList<Guide>> ListGuidesAsync(
        Guid gameId, CancellationToken token = default);
    // Newest activity first, then title, then ID. Reads only SQLite.
    Task<IReadOnlySet<string>> ListMapHashesAsync(CancellationToken token = default);
    // S22: where saved guides came from, and a game's Steam app ID.
    Task<GuideSource?> GetGuideSourceAsync(Guid guideId, CancellationToken token = default);
    Task<IReadOnlyDictionary<Guid, GuideSource>> ListGuideSourcesAsync(Guid gameId, CancellationToken token = default);
    Task<string?> GetSteamAppIdAsync(Guid gameId, CancellationToken token = default);
    Task SetSteamAppIdAsync(Guid gameId, string? appId, CancellationToken token = default);
    Task<IReadOnlyList<LibraryGameSummary>> ListGameSummariesAsync(
        CancellationToken token = default);
    Task<IReadOnlyList<GuideSummary>> ListGuideSummariesAsync(
        Guid gameId, CancellationToken token = default);
    Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default);
    Task<ReadingState?> GetReadingStateAsync(
        Guid guideId, CancellationToken token = default);
    Task<ReaderPreferences?> GetReaderPreferencesAsync(
        Guid guideId, CancellationToken token = default);
    Task SaveReaderPreferencesAsync(
        Guid guideId, double? textScale, CancellationToken token = default);
    // T19.2: changes only the view, so it never races a text-size save.
    Task SaveTextViewAsync(Guid guideId, TextViewMode view, CancellationToken token = default);
    Task<AppSettings> GetSettingsAsync(CancellationToken token = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default);
    Task<AppSettings> UpdateSettingsAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken token = default);
    Task<Game?> FindLinkedGameAsync(
        string provider, string externalId, CancellationToken token = default);
    Task<Game> AddLinkedGameAsync(NewLinkedGame game, CancellationToken token = default);
    Task<Game> UpdateGameMetadataAsync(
        Guid gameId, GameMetadataSnapshot metadata, DateTimeOffset retrievedUtc,
        string? artworkRelativePath, CancellationToken token = default);
}
