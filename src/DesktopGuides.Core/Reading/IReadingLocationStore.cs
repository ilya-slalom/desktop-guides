namespace DesktopGuides.Core.Reading;

// The writes the progress coordinator needs from the library.
public interface IReadingLocationStore
{
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);

    // Sets only the guide's last open time.
    Task RecordGuideOpenedAsync(
        Guid guideId, DateTimeOffset openedUtc, CancellationToken token = default);
}
