namespace DesktopGuides.Infrastructure.Online;

/// <summary>
/// A page as a browser loaded it: its HTTP status (0 for a timeout), its
/// DOM's HTML (null when it didn't load), and every image it received.
/// </summary>
public sealed record PageLoad(int Status, string? Html, IReadOnlyDictionary<Uri, byte[]> Images);

/// <summary>Loads pages and images the way a browser does, in one session.</summary>
public interface IPageLoader
{
    Task<PageLoad> LoadPageAsync(Uri url, CancellationToken token);

    Task<byte[]?> LoadImageAsync(Uri url, CancellationToken token);
}

public abstract record CaptureOutcome
{
    public sealed record Complete(CaptureDraft Draft) : CaptureOutcome;

    /// <summary>The site refused page <paramref name="Number"/> of <paramref name="Total"/>.</summary>
    public sealed record Paused(CaptureDraft Draft, GuidePageLink Page, int Number, int Total, int Status) : CaptureOutcome;
}

/// <summary>
/// S22: captures a draft's remaining pages one at a time, at least 2 s
/// apart, through the user's browser session, then the pages they link to
/// that the adapter saves with the guide, one level deep. A refused page (403, 429,
/// 503, a timeout, or a page the adapter can't read, such as a challenge)
/// pauses the capture there; every page already captured stays in the draft.
/// </summary>
public sealed class GuideCapture(
    IGuideSourceAdapter adapter, IPageLoader loader, TimeProvider clock,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static readonly TimeSpan Pace = TimeSpan.FromSeconds(2);
    private readonly Func<TimeSpan, CancellationToken, Task> delay =
        delay ?? ((wait, token) => Task.Delay(wait, clock, token));
    private DateTimeOffset? lastStart;

    public async Task<CaptureOutcome> RunAsync(
        CaptureDraft draft, IProgress<(int Done, int Total)>? progress, CancellationToken token)
    {
        // The guide's own pages come first; each adds the pages it links to,
        // which are captured after them and add none.
        while (draft.Remaining.FirstOrDefault() is { } page)
        {
            int number = IndexOf(draft, page) + 1;
            await PaceAsync(token);
            PageLoad load = await loader.LoadPageAsync(page.Url, token);
            bool loaded = load.Status is not (403 or 429 or 503 or 0) && load.Html is not null;
            GuidePageReading? reading = !loaded ? null
                : page.Linked ? adapter.ReadLinked(page, load.Html!, draft.Item)
                : adapter.Read(page.Url, load.Html!);
            if (reading is null && page.Linked && loaded && adapter.IsSitePage(load.Html!))
            {
                // The site's own page, but no page of the guide: its links stay online.
                draft.Drop(page.PageKey);
                progress?.Report((draft.Captured.Count, draft.Pages.Count));
                continue;
            }
            // A page read back as another page would never leave Remaining.
            if (reading is null || reading.Match.ItemId != draft.Item.ItemId || reading.Match.Site != draft.Item.Site ||
                reading.Match.PageKey != page.PageKey)
            {
                return new CaptureOutcome.Paused(draft, page, number, draft.Pages.Count, load.Status);
            }

            Dictionary<Uri, string> files = [];
            foreach (Uri image in reading.Images)
            {
                byte[]? bytes = load.Images.TryGetValue(image, out byte[]? carried) ? carried : null;
                if (bytes is null)
                {
                    await PaceAsync(token);
                    bytes = await loader.LoadImageAsync(image, token);
                }
                if (bytes is not null) files[image] = draft.SaveImage(image, bytes);
            }
            draft.Add(reading, files);
            if (!page.Linked) draft.Extend(reading.Linked);
            progress?.Report((draft.Captured.Count, draft.Pages.Count));
        }
        return new CaptureOutcome.Complete(draft);
    }

    private static int IndexOf(CaptureDraft draft, GuidePageLink page)
    {
        for (int index = 0; index < draft.Pages.Count; index++)
        {
            if (draft.Pages[index].PageKey == page.PageKey) return index;
        }
        return 0;
    }

    private async Task PaceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (lastStart is { } previous && clock.GetUtcNow() - previous is { } since && since < Pace)
        {
            await delay(Pace - since, token);
        }
        lastStart = clock.GetUtcNow();
    }
}
