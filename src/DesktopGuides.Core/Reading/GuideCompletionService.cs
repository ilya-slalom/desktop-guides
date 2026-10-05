namespace DesktopGuides.Core.Reading;

// Marks a guide complete or back in progress by explicit action only.
// Completion never follows from the estimate or the reading position.
public sealed class GuideCompletionService
{
    private readonly IGuideCompletionStore store;
    private readonly TimeProvider clock;

    public GuideCompletionService(IGuideCompletionStore store, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        this.store = store;
        this.clock = clock;
    }

    public Task<DateTimeOffset?> MarkCompleteAsync(Guid guideId, CancellationToken token = default) =>
        store.SetGuideCompletionAsync(guideId, clock.GetUtcNow(), token);

    public Task<DateTimeOffset?> MarkInProgressAsync(Guid guideId, CancellationToken token = default) =>
        store.SetGuideCompletionAsync(guideId, null, token);
}
