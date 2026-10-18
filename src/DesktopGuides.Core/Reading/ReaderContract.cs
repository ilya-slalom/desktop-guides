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
    SelectableText = 128,
    Outline = 256,
    // T19.2: TXT's Original or Reflow view.
    Reflow = 512,
    // T21.2: a web guide of several pages; its page commands move between them.
    GuidePages = 1024
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
    Find,
    Contents,
    Bookmarks,
    AddBookmark
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

public static class ReaderCommandPolicy
{
    private static readonly (ReaderCommand Command, ReaderCapabilities Required)[] Commands =
    [
        (ReaderCommand.Scroll, ReaderCapabilities.Scroll),
        (ReaderCommand.PageTurn, ReaderCapabilities.PageNavigation),
        (ReaderCommand.PageEdge, ReaderCapabilities.PageNavigation),
        (ReaderCommand.PageTurn, ReaderCapabilities.GuidePages),
        (ReaderCommand.PageEdge, ReaderCapabilities.GuidePages),
        (ReaderCommand.PageJump, ReaderCapabilities.PageJump),
        (ReaderCommand.FitWidth, ReaderCapabilities.FitWidth),
        (ReaderCommand.Zoom, ReaderCapabilities.Zoom),
        (ReaderCommand.TextSize, ReaderCapabilities.TextSize),
        (ReaderCommand.Find, ReaderCapabilities.Find),
        (ReaderCommand.Contents, ReaderCapabilities.Outline),
        (ReaderCommand.Bookmarks, ReaderCapabilities.Outline),
        (ReaderCommand.AddBookmark, ReaderCapabilities.Outline)
    ];

    public static IReadOnlyList<ReaderCommand> VisibleCommands(IReaderSession session) =>
        VisibleCommands(session.Capabilities);

    public static IReadOnlyList<ReaderCommand> VisibleCommands(ReaderCapabilities capabilities) =>
        Commands
            .Where(entry => (capabilities & entry.Required) == entry.Required)
            .Select(entry => entry.Command)
            .Distinct()
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

public enum PageKey { PageUp, PageDown, Home, End }

public enum PageKeyCommand { PreviousPage, NextPage, PageStart, PageEnd }

/// <summary>
/// T21.2: TXT and PDF page with Page Up/Down and Ctrl+Home/End. A web guide
/// of several pages leaves those to the page, which scrolls, and moves
/// between its pages with Ctrl+Page Up/Down (the user's choice).
/// </summary>
public static class PageKeyRouting
{
    public static PageKeyCommand? For(ReaderCapabilities capabilities, PageKey key, bool control)
    {
        if ((capabilities & ReaderCapabilities.GuidePages) != 0)
        {
            return (key, control) switch
            {
                (PageKey.PageUp, true) => PageKeyCommand.PreviousPage,
                (PageKey.PageDown, true) => PageKeyCommand.NextPage,
                _ => null
            };
        }
        if ((capabilities & ReaderCapabilities.PageNavigation) == 0) return null;
        return (key, control) switch
        {
            (PageKey.PageUp, false) => PageKeyCommand.PreviousPage,
            (PageKey.PageDown, false) => PageKeyCommand.NextPage,
            (PageKey.Home, true) => PageKeyCommand.PageStart,
            (PageKey.End, true) => PageKeyCommand.PageEnd,
            _ => null
        };
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
