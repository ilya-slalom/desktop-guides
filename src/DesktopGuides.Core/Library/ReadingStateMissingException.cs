namespace DesktopGuides.Core.Library;

// The guide's ReadingStates row is gone, usually because the guide was removed.
public sealed class ReadingStateMissingException(Guid guideId)
    : InvalidOperationException($"Guide {guideId} has no reading state.")
{
    public Guid GuideId { get; } = guideId;
}
