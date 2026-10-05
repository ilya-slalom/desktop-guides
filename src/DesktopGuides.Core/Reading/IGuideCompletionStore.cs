namespace DesktopGuides.Core.Reading;

// The completion write the library provides.
public interface IGuideCompletionStore
{
    // Null clears the completion time. A time is kept only when the guide
    // isn't already complete. Returns the committed completion time.
    Task<DateTimeOffset?> SetGuideCompletionAsync(
        Guid guideId, DateTimeOffset? completedUtc, CancellationToken token = default);
}
