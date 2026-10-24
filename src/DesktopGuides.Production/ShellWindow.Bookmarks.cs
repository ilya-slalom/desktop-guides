using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Infrastructure.Storage;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T19.1: the open guide's bookmarks, in the pane's Bookmarks tab. The tab
    // last chosen lasts for the app session, like the pane's open state.
    private const string BookmarksLoadFailed = "Couldn't load this guide's bookmarks.";
    private ReaderPaneTab paneTab = ReaderPaneTab.Contents;
    private Guid? bookmarkGuide;
    private GuideFormat bookmarkFormat;
    private string bookmarkSha = string.Empty;
    private IReadOnlyCollection<string>? bookmarkDocuments;
    private IReadOnlyList<BookmarkItem> bookmarks = [];
    private bool addingBookmark;
    private Func<Task>? undoAction;
    private DispatcherQueueTimer? undoTimer;

    private void InitializeBookmarks()
    {
        BookmarksPane tab = ReaderContents.BookmarksTab;
        tab.AddRequested += (_, _) => _ = AddBookmarkAsync();
        tab.ItemInvoked += (_, item) => _ = FollowBookmarkAsync(item);
        tab.RenameRequested += (_, change) => _ = RenameBookmarkAsync(change.Item, change.Label);
        tab.DeleteRequested += (_, item) => _ = DeleteBookmarkAsync(item);
        ReaderContents.BookmarksRequested += (_, _) => _ = OpenPaneAsync(ReaderPaneTab.Bookmarks);
        ReaderContents.TabChanged += (_, _) =>
        {
            paneTab = ReaderContents.Tab;
            if (paneTab == ReaderPaneTab.Bookmarks) _ = RefreshBookmarksAsync(select: null, focus: false);
            else _ = ShowContentsTabAsync();
        };
        ReaderActions.BookmarksRequested += (_, _) => TogglePane(ReaderPaneTab.Bookmarks);
        ReaderActions.AddBookmarkRequested += (_, _) => _ = AddBookmarkAsync();
        undoTimer = DispatcherQueue.CreateTimer();
        undoTimer.Interval = BookmarkList.UndoWindow;
        undoTimer.IsRepeating = false;
        // Another status may have replaced the Undo meanwhile; that one stays.
        undoTimer.Tick += (_, _) =>
        {
            if (undoAction is not null && ReferenceEquals(statusAction, undoAction)) HideStatus();
            undoAction = null;
        };
    }

    // Called by StartContents, once the guide's tracking has started.
    private void StartBookmarks(Guide guide, string contentSha256, IReadOnlyCollection<string>? htmlDocuments)
    {
        bookmarkGuide = guide.Id;
        bookmarkFormat = guide.Format;
        bookmarkSha = contentSha256;
        bookmarkDocuments = htmlDocuments;
        bookmarks = [];
        ReaderContents.BookmarksTab.Clear();
    }

    // A pending Undo is kept: it restores into its own guide.
    private void StopBookmarks()
    {
        bookmarkGuide = null;
        bookmarks = [];
        ReaderContents.BookmarksTab.Clear();
    }

    private bool AddBookmarkFromKeyboard()
    {
        if (bookmarkGuide is null || readerSession is not IOutlineReader) return false;
        _ = AddBookmarkAsync();
        return true;
    }

    private bool ToggleBookmarksFromKeyboard()
    {
        if (bookmarkGuide is null || readerSession is not IOutlineReader) return false;
        TogglePane(ReaderPaneTab.Bookmarks);
        return true;
    }

    private async Task OpenBookmarksAsync(Guid? select)
    {
        if (bookmarkGuide is null || readerSession is not IOutlineReader) return;
        paneTab = ReaderPaneTab.Bookmarks;
        ChoosePane(open: true);
        ReaderContents.ShowTab(ReaderPaneTab.Bookmarks);
        ShowContentsHost();
        await RefreshBookmarksAsync(select, focus: true);
    }

    private async Task<IReadOnlyList<BookmarkItem>?> LoadBookmarksAsync()
    {
        if (bookmarkGuide is not Guid guide || readerSession is not IOutlineReader reader ||
            repository is not SqliteLibraryRepository store)
        {
            return null;
        }
        IReadOnlyList<Bookmark> stored;
        try
        {
            stored = await store.ListBookmarksAsync(guide);
        }
        catch (Exception error)
        {
            RecordBookmarkFailureForTest("list", error);
            ShowErrorStatus(BookmarksLoadFailed);
            return null;
        }
        // The guide was left, or another opened, while the list loaded.
        if (bookmarkGuide != guide || !ReferenceEquals(reader, readerSession)) return null;
        bookmarks = BookmarkList.Arrange(stored,
            json => ReaderLocationCodec.Deserialize(json, bookmarkFormat, bookmarkSha, bookmarkDocuments),
            reader.OrderOf);
        return bookmarks;
    }

    private async Task RefreshBookmarksAsync(Guid? select, bool focus)
    {
        if (await LoadBookmarksAsync() is not IReadOnlyList<BookmarkItem> items) return;
        ReaderContents.BookmarksTab.ShowItems(items, select, BookmarkPageTitleOf());
        if (focus) await ReaderContents.BookmarksTab.FocusContentAsync();
    }

    // One add at a time: a double click must not make two bookmarks at one place.
    private async Task AddBookmarkAsync()
    {
        if (addingBookmark) return;
        addingBookmark = true;
        try
        {
            await AddBookmarkOnceAsync();
        }
        finally
        {
            addingBookmark = false;
        }
    }

    private async Task AddBookmarkOnceAsync()
    {
        if (bookmarkGuide is not Guid guide || readerSession is not IReaderSession session ||
            session is not IOutlineReader reader || repository is not SqliteLibraryRepository store)
        {
            return;
        }
        ReaderLocation place;
        try
        {
            place = await session.GetLocationAsync(CancellationToken.None);
        }
        catch (Exception error)
        {
            RecordBookmarkFailureForTest("location", error);
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        if (await LoadBookmarksAsync() is not IReadOnlyList<BookmarkItem> items) return;
        long order = reader.OrderOf(place);
        if (BookmarkList.At(items, order) is BookmarkItem existing)
        {
            await OpenBookmarksAsync(existing.Bookmark.Id);
            AnnounceStatus(BookmarkList.AlreadyBookmarked);
            return;
        }
        // While the outline builds there are no entries yet: the place's own words.
        string label = BookmarkList.DefaultLabel(place, outline ?? Outline.Empty, order);
        Bookmark added;
        try
        {
            added = await store.AddBookmarkAsync(guide, ReaderLocationCodec.Serialize(place), label);
        }
        catch (BookmarkLimitException)
        {
            ShowWarningStatus(BookmarkList.Full);
            return;
        }
        catch (Exception error)
        {
            RecordBookmarkFailureForTest("add", error);
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        await OpenBookmarksAsync(added.Id);
        AnnounceStatus(BookmarkList.Added);
    }

    // A deliberate move: the restore raises LocationChanged, so the place saves.
    private async Task FollowBookmarkAsync(BookmarkItem item)
    {
        if (item.Location is not ReaderLocation place || readerSession is not IReaderSession session) return;
        RestoreOutcome outcome;
        try
        {
            outcome = await session.RestoreLocationAsync(place, CancellationToken.None);
        }
        catch (Exception)
        {
            outcome = new RestoreOutcome(RestoreKind.Unavailable, null);
        }
        if (!ReferenceEquals(session, readerSession)) return;
        if (outcome.Kind == RestoreKind.Approximate) ShowReaderNotice(BookmarkList.Shifted);
        else if (outcome.Kind == RestoreKind.Unavailable) ShowWarningStatus(BookmarkList.NotFound);
        if (ContentsNarrow) CloseContents(focusGuide: false, chosen: false);
        FocusReaderContent();
    }

    private async Task RenameBookmarkAsync(BookmarkItem item, string label)
    {
        if (repository is not SqliteLibraryRepository store) return;
        try
        {
            await store.RenameBookmarkAsync(item.Bookmark.Id, label);
        }
        catch (Exception error)
        {
            RecordBookmarkFailureForTest("rename", error);
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        await RefreshBookmarksAsync(item.Bookmark.Id, focus: true);
    }

    private async Task DeleteBookmarkAsync(BookmarkItem item)
    {
        if (repository is not SqliteLibraryRepository store) return;
        Guid id = item.Bookmark.Id;
        BookmarkItem? next = bookmarks.SkipWhile(b => b.Bookmark.Id != id).Skip(1).FirstOrDefault()
            ?? bookmarks.TakeWhile(b => b.Bookmark.Id != id).LastOrDefault();
        try
        {
            await store.DeleteBookmarkAsync(id);
        }
        catch (Exception error)
        {
            RecordBookmarkFailureForTest("delete", error);
            ShowErrorStatus(BookmarkList.SaveFailed);
            return;
        }
        await RefreshBookmarksAsync(next?.Bookmark.Id, focus: true);
        Bookmark deleted = item.Bookmark;
        Func<Task> undo = null!;
        undo = async () =>
        {
            // A second click, or one after the window closed, does nothing.
            if (!ReferenceEquals(undoAction, undo)) return;
            undoAction = null;
            undoTimer?.Stop();
            HideStatus();
            try
            {
                await store.RestoreBookmarkAsync(deleted);
            }
            catch (BookmarkLimitException)
            {
                ShowWarningStatus(BookmarkList.Full);
                return;
            }
            catch (Exception error)
            {
                RecordBookmarkFailureForTest("restore", error);
                ShowErrorStatus(BookmarkList.SaveFailed);
                return;
            }
            // The Undo button just hid: focus goes to the list if it shows, else the guide.
            bool showing = ReaderContentsHost.Visibility == Visibility.Visible &&
                ReaderContents.Tab == ReaderPaneTab.Bookmarks;
            if (bookmarkGuide == deleted.GuideId) await RefreshBookmarksAsync(deleted.Id, focus: showing);
            if (!showing) FocusReaderContent();
        };
        ShowStatus(BookmarkList.Deleted, InfoBarSeverity.Informational, isClosable: true, autoDismiss: false);
        ShowStatusAction(BookmarkList.UndoLabel, undo);
        undoAction = undo;
        undoTimer?.Stop();
        undoTimer?.Start();
    }

    // Test gate only: what a failed bookmark operation threw, so a test that
    // sees "Couldn't load…" or "Couldn't save…" can say why.
    private void RecordBookmarkFailureForTest(string stage, Exception error)
    {
        if (cacheRoot is null ||
            !TestGate.IsOpen(AppLane.Current.LocalEvent("ProgressDiagnostics", Environment.ProcessId)))
        {
            return;
        }
        try
        {
            using MemoryStream buffer = new();
            using (Utf8JsonWriter json = new(buffer))
            {
                json.WriteStartObject();
                json.WriteString("stage", stage);
                json.WriteString("type", error.GetType().FullName);
                json.WriteString("hresult", $"0x{error.HResult:X8}");
                json.WriteString("message", error.Message);
                json.WriteEndObject();
            }
            string folder = Path.Combine(cacheRoot, "diagnostics");
            Directory.CreateDirectory(folder);
            DiagnosticFile.Write(
                Path.Combine(folder, $"bookmarks-{Environment.ProcessId}.json"),
                System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Test diagnostics must never affect reading.
        }
    }

    // T21.2: a guide of several pages names each bookmark's page.
    private Func<BookmarkItem, string?>? BookmarkPageTitleOf() =>
        readerSession is HtmlReaderSession { Pages.Count: > 1 } html
            ? item => item.Location?.Payload is HtmlPosition position &&
                html.Pages.IndexOfDocument(position.DocumentPath) is int page
                    ? html.Pages.DisplayTitles[page]
                    : null
            : null;
}
