namespace DesktopGuides.Core.Pdf;

/// <summary>
/// Runs page loads one at a time. While a load runs, newer requests replace
/// one another, and a result is applied only if nothing newer was asked for.
/// Call Request and CancelAsync from one thread; apply and failed must not
/// throw.
/// </summary>
public sealed class LatestWinsScheduler<TResult>(
    Func<int, CancellationToken, Task<TResult>> load,
    Action<int, TResult> apply,
    Action<int, Exception> failed)
{
    private readonly object gate = new();
    private readonly CancellationTokenSource cancel = new();
    private Task running = Task.CompletedTask;
    private int? waiting;
    private bool loading;
    private bool cancelled;
    private int requests;
    private int loads;
    private int staleResults;

    public int Requests { get { lock (gate) return requests; } }
    public int Loads { get { lock (gate) return loads; } }
    public int StaleResults { get { lock (gate) return staleResults; } }

    public void Request(int pageIndex)
    {
        lock (gate)
        {
            if (cancelled) return;
            requests++;
            waiting = pageIndex;
            if (loading) return;
            loading = true;
        }
        running = RunAsync();
    }

    public async Task CancelAsync()
    {
        bool first;
        lock (gate)
        {
            first = !cancelled;
            cancelled = true;
            waiting = null;
        }
        if (first) cancel.Cancel();
        await running;
    }

    private async Task RunAsync()
    {
        while (true)
        {
            int page;
            lock (gate)
            {
                // Clearing loading under the same lock that sees no waiting
                // request means the next Request starts a fresh run.
                if (cancelled || waiting is null)
                {
                    loading = false;
                    return;
                }
                page = waiting.Value;
                waiting = null;
                loads++;
            }
            TResult result = default!;
            Exception? error = null;
            try
            {
                result = await load(page, cancel.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                lock (gate) loading = false;
                return;
            }
            catch (Exception exception)
            {
                error = exception;
            }
            bool current;
            lock (gate)
            {
                current = waiting is null && !cancelled;
                if (!current && !cancelled) staleResults++;
            }
            if (!current) continue;
            if (error is null) apply(page, result);
            else failed(page, error);
        }
    }
}
