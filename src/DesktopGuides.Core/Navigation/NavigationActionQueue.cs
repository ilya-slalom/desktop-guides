namespace DesktopGuides.Core.Navigation;

public sealed class NavigationActionQueue : IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private TaskCompletionSource? idle;
    private int pending;
    private bool stopped;

    public async Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (sync)
        {
            if (stopped)
            {
                return;
            }
            if (pending++ == 0)
            {
                idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        try
        {
            await gate.WaitAsync();
            try
            {
                await action();
            }
            finally
            {
                gate.Release();
            }
        }
        finally
        {
            lock (sync)
            {
                if (--pending == 0)
                {
                    idle!.SetResult();
                }
            }
        }
    }

    public Task StopAndDrainAsync()
    {
        lock (sync)
        {
            stopped = true;
            return idle?.Task ?? Task.CompletedTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAndDrainAsync();
        gate.Dispose();
    }
}
