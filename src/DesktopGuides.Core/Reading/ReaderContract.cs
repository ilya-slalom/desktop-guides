using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Reading;

[Flags]
public enum ReaderCapabilities
{
    None = 0,
    Scroll = 1,
    PageNavigation = 2,
    PageJump = 4,
    FitWidth = 8,
    Zoom = 16,
    TextSize = 32,
    Find = 64,
    SelectableText = 128
}

public enum ReaderCommand
{
    Scroll,
    PageTurn,
    PageJump,
    FitWidth,
    Zoom,
    TextSize,
    Find
}

public abstract record ReaderAction(ReaderCommand Command);
public sealed record ScrollAction(double VerticalViewportFraction)
    : ReaderAction(ReaderCommand.Scroll);
public sealed record PageTurnAction(int Delta)
    : ReaderAction(ReaderCommand.PageTurn);
public sealed record PageJumpAction(int PageNumber)
    : ReaderAction(ReaderCommand.PageJump);
public sealed record FitWidthAction()
    : ReaderAction(ReaderCommand.FitWidth);
public sealed record ZoomAction(double Factor)
    : ReaderAction(ReaderCommand.Zoom);
public sealed record TextSizeAction(double Factor)
    : ReaderAction(ReaderCommand.TextSize);
public sealed record FindAction(string Query)
    : ReaderAction(ReaderCommand.Find);

public static class ReaderCommandPolicy
{
    private static readonly (ReaderCommand Command, ReaderCapabilities Required)[] Commands =
    [
        (ReaderCommand.Scroll, ReaderCapabilities.Scroll),
        (ReaderCommand.PageTurn, ReaderCapabilities.PageNavigation),
        (ReaderCommand.PageJump, ReaderCapabilities.PageJump),
        (ReaderCommand.FitWidth, ReaderCapabilities.FitWidth),
        (ReaderCommand.Zoom, ReaderCapabilities.Zoom),
        (ReaderCommand.TextSize, ReaderCapabilities.TextSize),
        (ReaderCommand.Find, ReaderCapabilities.Find)
    ];

    public static IReadOnlyList<ReaderCommand> VisibleCommands(IReaderSession session) =>
        Commands
            .Where(entry => (session.Capabilities & entry.Required) == entry.Required)
            .Select(entry => entry.Command)
            .ToArray();

    public static Task ExecuteAsync(
        IReaderSession session, ReaderAction action, CancellationToken token)
    {
        if (!VisibleCommands(session).Contains(action.Command))
        {
            throw new NotSupportedException(
                $"This reader does not support {action.Command}.");
        }
        return session.ExecuteAsync(action, token);
    }
}

public sealed record ManagedGuideSource(Guide Guide, string PrimaryFilePath);

public sealed record ReaderAppearance(ThemePreference Theme, double TextScale);

public sealed class LocationChangedEventArgs : EventArgs;

public interface IReaderSession : IAsyncDisposable
{
    GuideFormat Format { get; }
    ReaderCapabilities Capabilities { get; }
    event EventHandler? CapabilitiesChanged;
    event EventHandler<LocationChangedEventArgs>? LocationChanged;
    Task OpenAsync(ManagedGuideSource source, CancellationToken token);
    Task<ReaderLocation> GetLocationAsync(CancellationToken token);
    Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token);
    Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token);
    Task ExecuteAsync(ReaderAction action, CancellationToken token);
}
