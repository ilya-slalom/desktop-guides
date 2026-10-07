using DesktopGuides.Core.Library;
using DesktopGuides.Core.Navigation;
using DesktopGuides.Core.Reading;

namespace DesktopGuides.Production;

// The only caller of GuideCompletionService. The write runs in the
// navigation queue, so it is serialized with route changes.
public sealed partial class ShellWindow
{
    private GuideCompletionService? completion;
    private bool completionRequested;

    private async void CompletionChoiceRequested(object? sender, GuideCompletionRequest request)
    {
        if (sender is not GuideCompletionChoice choice)
        {
            return;
        }
        GuideCompletionService? service = completion;
        if (service is null || completionRequested || closeRequested ||
            removeRequested || importRequested)
        {
            choice.Revert();
            return;
        }
        ShellRoute? route = navigator.Current;
        completionRequested = true;
        GameCompletionChoice.SetBusy(true);
        ReaderCompletionChoice.SetBusy(true);
        bool written = false;
        try
        {
            await RunNavigationAsync(async () =>
            {
                written = true;
                DateTimeOffset? committed;
                try
                {
                    committed = request.Complete
                        ? await service.MarkCompleteAsync(request.GuideId)
                        : await service.MarkInProgressAsync(request.GuideId);
                }
                catch (ReadingStateMissingException)
                {
                    if (closeRequested)
                    {
                        return;
                    }
                    // The Game page drops the row; the Reader returns to the Library.
                    await RenderCurrentAsync();
                    ShowWarningStatus(GuideCompletionPresentation.Removed);
                    return;
                }
                catch (Exception)
                {
                    if (closeRequested)
                    {
                        return;
                    }
                    if (choice.GuideId == request.GuideId)
                    {
                        choice.Revert();
                    }
                    ShowErrorStatus(GuideCompletionPresentation.SaveFailed(request.Title));
                    return;
                }
                if (closeRequested || !Equals(navigator.Current, route))
                {
                    // The next render shows the committed state.
                    return;
                }
                string announcement = GuideCompletionPresentation.Announcement(request.Title, committed);
                if (route is GameRoute)
                {
                    // The render refreshes the row fact and the choice; it
                    // collapses the panel, so focus is put back.
                    await RenderCurrentAsync();
                    if (GameCompletionChoice.GuideId == request.GuideId)
                    {
                        GameCompletionChoice.FocusSelection();
                    }
                    AnnounceStatus(announcement);
                }
                else if (route is ReaderRoute reader && reader.GuideId == request.GuideId)
                {
                    ReaderCompletionChoice.Show(
                        request.GuideId, request.Title, GuideCompletionPresentation.IsComplete(committed));
                    AnnounceStatus(announcement);
                }
            });
        }
        finally
        {
            completionRequested = false;
            GameCompletionChoice.SetBusy(false);
            ReaderCompletionChoice.SetBusy(false);
            // RunNavigationAsync skips the action once a close begins.
            if (!written && !closeRequested && choice.GuideId == request.GuideId)
            {
                choice.Revert();
            }
        }
    }
}
