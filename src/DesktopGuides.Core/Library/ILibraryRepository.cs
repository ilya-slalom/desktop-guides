namespace DesktopGuides.Core.Library;

public interface ILibraryRepository : IAsyncDisposable
{
    StartupReconciliationReport? LastStartupReconciliation { get; }
    Task InitializeAsync(CancellationToken token = default);
    Task<Game> AddGameAsync(
        string title, string? platform, string? notes, CancellationToken token = default);
    Task<IReadOnlyList<Game>> ListGamesAsync(CancellationToken token = default);
    Task<Game?> GetGameAsync(Guid gameId, CancellationToken token = default);
    Task<IReadOnlyList<Guide>> ListGuidesAsync(
        Guid gameId, CancellationToken token = default);
    Task<Guide?> GetGuideAsync(Guid guideId, CancellationToken token = default);
    Task<ReadingState?> GetReadingStateAsync(
        Guid guideId, CancellationToken token = default);
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);
    Task<ReaderPreferences?> GetReaderPreferencesAsync(
        Guid guideId, CancellationToken token = default);
    Task SaveReaderPreferencesAsync(
        Guid guideId, double? textScale, CancellationToken token = default);
    Task<AppSettings> GetSettingsAsync(CancellationToken token = default);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken token = default);
}
