namespace DesktopGuides.Core.Reading;

// The one write the progress coordinator needs from the library.
public interface IReadingLocationStore
{
    Task SaveReadingLocationAsync(
        Guid guideId, string locatorJson, double? estimatedFraction,
        CancellationToken token = default);
}
