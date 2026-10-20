using Microsoft.UI.Dispatching;

namespace DesktopGuides.Production;

// #138: a list or tree just shown may not have made the container to focus
// on the first layout pass after it. Each try runs at low priority, after
// layout; a few tries pass before the fallback takes focus instead.
internal static class FocusRetry
{
    public const int Attempts = 6;

    public static Task RunAsync(DispatcherQueue queue, Func<bool> tryFocus, Action fallback, int attempts = Attempts)
    {
        ArgumentNullException.ThrowIfNull(queue);
        TaskCompletionSource done = new();
        void Attempt(int left)
        {
            bool queued = queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                if (tryFocus())
                {
                    done.TrySetResult();
                    return;
                }
                if (left > 1)
                {
                    Attempt(left - 1);
                    return;
                }
                fallback();
                done.TrySetResult();
            });
            if (!queued) done.TrySetResult();
        }
        Attempt(Math.Max(1, attempts));
        return done.Task;
    }
}
