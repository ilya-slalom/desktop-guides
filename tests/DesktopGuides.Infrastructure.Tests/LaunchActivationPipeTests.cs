using System.Threading.Channels;
using DesktopGuides.Infrastructure.Activation;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LaunchActivationPipeTests
{
    [Fact]
    public async Task LateReplyCannotAcknowledgeAnotherLaunch()
    {
        string name = $"DesktopGuides.Tests.Activation.{Guid.NewGuid():N}";
        Channel<LaunchActivationPipe.Request> requests =
            Channel.CreateUnbounded<LaunchActivationPipe.Request>();
        using LaunchActivationPipe server =
            new(name, request => requests.Writer.TryWrite(request));

        Task<bool> first = Task.Run(() => LaunchActivationPipe.TryRequest(
            name, DateTime.UtcNow.AddMilliseconds(500)));
        LaunchActivationPipe.Request firstRequest =
            await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(10)));

        Task<bool> second = Task.Run(() => LaunchActivationPipe.TryRequest(
            name, DateTime.UtcNow.AddSeconds(10)));
        LaunchActivationPipe.Request secondRequest =
            await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        firstRequest.Complete(true);
        await Task.Delay(150);
        Assert.False(second.IsCompleted);
        Assert.True(secondRequest.Complete(true));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task AcceptedLaunchStaysAcceptedAfterServerStops()
    {
        string name = $"DesktopGuides.Tests.Activation.{Guid.NewGuid():N}";
        Channel<LaunchActivationPipe.Request> requests =
            Channel.CreateUnbounded<LaunchActivationPipe.Request>();
        using LaunchActivationPipe server =
            new(name, request => requests.Writer.TryWrite(request));

        Task<bool> launch = Task.Run(() => LaunchActivationPipe.TryRequest(
            name, DateTime.UtcNow.AddSeconds(10)));
        LaunchActivationPipe.Request request =
            await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(request.Complete(true));
        server.Stop();
        Assert.True(await launch.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task PendingLaunchRetriesWhenServerStops()
    {
        string name = $"DesktopGuides.Tests.Activation.{Guid.NewGuid():N}";
        Channel<LaunchActivationPipe.Request> requests =
            Channel.CreateUnbounded<LaunchActivationPipe.Request>();
        using LaunchActivationPipe server =
            new(name, request => requests.Writer.TryWrite(request));

        Task<bool> launch = Task.Run(() => LaunchActivationPipe.TryRequest(
            name, DateTime.UtcNow.AddSeconds(10)));
        _ = await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

        server.Stop();
        Assert.False(await launch.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
