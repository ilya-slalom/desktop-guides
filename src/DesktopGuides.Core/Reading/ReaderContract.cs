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
    PageEdge,
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

public enum ReaderEdge
{
    Start,
    End
}

public sealed record PageEdgeAction(ReaderEdge Edge)
    : ReaderAction(ReaderCommand.PageEdge);

public sealed record PageJumpAction(int PageNumber)
    : ReaderAction(ReaderCommand.PageJump);
public sealed record FitWidthAction()
    : ReaderAction(ReaderCommand.FitWidth);
public sealed record ZoomAction(double Factor)
    : ReaderAction(ReaderCommand.Zoom);
// The target scale, not a factor: the shell steps, the sessions apply.
public sealed record TextSizeAction(double Scale)
    : ReaderAction(ReaderCommand.TextSize)
{
    public double Scale { get; } =
        double.IsFinite(Scale) && Scale >= TextSizeSteps.Min && Scale <= TextSizeSteps.Max
            ? Scale
            : throw new ArgumentOutOfRangeException(
                nameof(Scale), Scale, "A text size must be between 75% and 200%.");
}
public sealed record FindAction(string Query)
    : ReaderAction(ReaderCommand.Find);

public static class ReaderCommandPolicy
{
    private static readonly (ReaderCommand Command, ReaderCapabilities Required)[] Commands =
    [
        (ReaderCommand.Scroll, ReaderCapabilities.Scroll),
        (ReaderCommand.PageTurn, ReaderCapabilities.PageNavigation),
        (ReaderCommand.PageEdge, ReaderCapabilities.PageNavigation),
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

    // T14.4: the toolbar's separator sits between movement and size or
    // zoom, so it shows only when a format has both.
    public static bool ShowsGroupSeparator(IReadOnlyCollection<ReaderCommand> visible) =>
        (visible.Contains(ReaderCommand.PageTurn) || visible.Contains(ReaderCommand.PageEdge)) &&
        (visible.Contains(ReaderCommand.TextSize) || visible.Contains(ReaderCommand.Zoom));

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

// The theme a reader paints with. The shell resolves System before it
// calls a reader, so a reader never reads the Windows theme itself.
public enum ReaderTheme { Light, Dark, HighContrast }

public sealed record ReaderAppearance(ReaderTheme Theme, double TextScale);

public sealed class LocationChangedEventArgs : EventArgs;

// T14.3: raised after a session scrolled back to its place following an
// appearance change, with how closely it came back.
public sealed class AppearanceRestoredEventArgs(RestoreOutcome outcome) : EventArgs
{
    public RestoreOutcome Outcome { get; } = outcome;
}

public interface IReaderSession : IAsyncDisposable
{
    GuideFormat Format { get; }
    ReaderCapabilities Capabilities { get; }
    event EventHandler? CapabilitiesChanged;
    event EventHandler<LocationChangedEventArgs>? LocationChanged;
    event EventHandler<AppearanceRestoredEventArgs>? AppearanceRestored;
    Task OpenAsync(ManagedGuideSource source, CancellationToken token);
    Task<ReaderLocation> GetLocationAsync(CancellationToken token);
    Task<RestoreOutcome> RestoreLocationAsync(ReaderLocation location, CancellationToken token);
    Task ApplyAppearanceAsync(ReaderAppearance appearance, CancellationToken token);
    Task ExecuteAsync(ReaderAction action, CancellationToken token);
}
