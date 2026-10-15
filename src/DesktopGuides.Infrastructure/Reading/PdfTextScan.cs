using DesktopGuides.Core.Reading;

namespace DesktopGuides.Infrastructure.Reading;

public interface IPdfPageTextReader
{
    int PageCount { get; }
    Task<PdfPageText> GetPageTextAsync(int pageIndex, CancellationToken token);
}

public readonly record struct PdfMatch(int PageIndex, int Start, int Length);

/// <summary>
/// T18.1: one query's scan of a PDF, from the first page to the last, on a
/// worker. Matches are found in page order, so an index never changes once
/// found. A step waits only as far as the scan must go to answer it.
/// </summary>
public sealed class PdfTextScan : IDisposable
{
    private readonly object sync = new();
    private readonly List<PdfMatch> matches = [];
    private readonly List<int> unreadable = [];
    private readonly CancellationTokenSource stop = new();
    private TaskCompletionSource progressed = NewSignal();
    private int scanned;
    private int imageOnly;
    private bool complete;
    private bool stopped;

    public PdfTextScan(IPdfPageTextReader pages, FindQuery query, int maxMatches = TextMatcher.MaxMatches)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(query);
        CancellationToken token = stop.Token;
        Completion = Task.Run(() => RunAsync(pages, query, maxMatches, token));
    }

    public Task Completion { get; }
    public event EventHandler? Finished;

    public async Task<int?> StepAsync(int? current, int fromPage, FindDirection direction, CancellationToken token)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                if (stopped) throw new OperationCanceledException();
                if (TryStep(current, fromPage, direction, out int? answer)) return answer;
                wait = progressed.Task;
            }
            await wait.WaitAsync(token).ConfigureAwait(false);
        }
    }

    public PdfMatch MatchAt(int index)
    {
        lock (sync) return matches[index];
    }

    public FindResult ResultFor(int? index)
    {
        lock (sync)
        {
            FindOutcome outcome = index is null ? FindOutcome.NotFound : FindOutcome.Found;
            return new FindResult(outcome, index + 1, matches.Count, !complete, imageOnly)
            {
                UnreadablePages = unreadable.Select(page => page + 1).ToArray()
            };
        }
    }

    public void Dispose() => stop.Cancel();

    // Under the lock. True when the scan so far settles the answer.
    private bool TryStep(int? current, int fromPage, FindDirection direction, out int? answer)
    {
        answer = null;
        int count = matches.Count;
        if (count == 0) return complete;
        if (current is int at)
        {
            int next = direction == FindDirection.Next ? at + 1 : at - 1;
            if (next >= 0 && next < count)
            {
                answer = next;
                return true;
            }
            if (!complete) return false;
            answer = direction == FindDirection.Next ? 0 : count - 1;
            return true;
        }
        if (direction == FindDirection.Next)
        {
            int first = matches.FindIndex(match => match.PageIndex >= fromPage);
            if (first >= 0)
            {
                answer = first;
                return true;
            }
            if (!complete) return false;
            answer = 0;
            return true;
        }
        // Matches before fromPage are all known once those pages are read.
        if (!complete && scanned < fromPage) return false;
        int last = matches.FindLastIndex(match => match.PageIndex < fromPage);
        if (last >= 0)
        {
            answer = last;
            return true;
        }
        if (!complete) return false;
        answer = count - 1;
        return true;
    }

    private async Task RunAsync(IPdfPageTextReader pages, FindQuery query, int maxMatches, CancellationToken token)
    {
        try
        {
            for (int page = 0; page < pages.PageCount; page++)
            {
                PdfPageText? text = null;
                try
                {
                    text = await pages.GetPageTextAsync(page, token).ConfigureAwait(false);
                }
                catch (PdfPageTextException)
                {
                    // A broken page is reported; the next may read.
                }
                int room;
                lock (sync) room = maxMatches - matches.Count;
                IReadOnlyList<TextMatch> found = text is { HasLetters: true }
                    ? TextMatcher.FindAll(text.Text, query, room)
                    : [];
                lock (sync)
                {
                    if (text is null) unreadable.Add(page);
                    else if (!text.HasLetters) imageOnly++;
                    foreach (TextMatch match in found) matches.Add(new PdfMatch(page, match.Start, match.Length));
                    scanned = page + 1;
                    if (matches.Count >= maxMatches) break;
                    Signal();
                }
            }
            lock (sync)
            {
                complete = true;
                Signal();
            }
            Finished?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Cancelled, or the source closed under the scan: waiting steps stop.
            lock (sync)
            {
                stopped = true;
                Signal();
            }
        }
    }

    private void Signal()
    {
        progressed.TrySetResult();
        progressed = NewSignal();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
