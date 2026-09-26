using DesktopGuides.Core.Navigation;
using Xunit;

namespace DesktopGuides.Core.Tests;

public sealed class NavigationActionQueueTests
{
    [Fact]
    public async Task StopWaitsForQueuedActionsAndRejectsLaterActions()
    {
        NavigationActionQueue queue = new();
        TaskCompletionSource firstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string> order = [];

        Task first = queue.RunAsync(async () =>
        {
            order.Add("first");
            firstStarted.SetResult();
            await releaseFirst.Task;
        });
        await firstStarted.Task;
        Task second = queue.RunAsync(() =>
        {
            order.Add("second");
            return Task.CompletedTask;
        });

        Task drained = queue.StopAndDrainAsync();
        await queue.RunAsync(() =>
        {
            order.Add("rejected");
            return Task.CompletedTask;
        });

        Assert.False(second.IsCompleted);
        Assert.False(drained.IsCompleted);
        Assert.Equal(["first"], order);

        releaseFirst.SetResult();
        await Task.WhenAll(first, second, drained);
        Assert.Equal(["first", "second"], order);
    }

    [Fact]
    public async Task FaultedActionDoesNotBlockLaterActionsOrDrain()
    {
        NavigationActionQueue queue = new();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.RunAsync(() => throw new InvalidOperationException("test")));

        bool ran = false;
        await queue.RunAsync(() =>
        {
            ran = true;
            return Task.CompletedTask;
        });
        await queue.StopAndDrainAsync();

        Assert.True(ran);
    }
}
