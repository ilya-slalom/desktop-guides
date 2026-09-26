using System.Collections.Concurrent;
using System.IO.Pipes;

namespace DesktopGuides.Infrastructure.Activation;

/// <summary>
/// A launch-only, same-user request channel. Each connection carries its own
/// acceptance reply so a delayed UI callback cannot acknowledge another launch.
/// </summary>
public sealed class LaunchActivationPipe : IDisposable
{
    private readonly string name;
    private readonly Func<Request, bool> dispatch;
    private readonly CancellationTokenSource stopped = new();
    private readonly ConcurrentDictionary<Request, byte> pending = new();
    private readonly Task acceptLoop;
    private int stopRequested;

    public LaunchActivationPipe(string name, Func<Request, bool> dispatch)
    {
        this.name = name;
        this.dispatch = dispatch;
        acceptLoop = AcceptAsync(CreateListener());
    }

    public static bool TryRequest(string name, DateTime deadline)
    {
        using NamedPipeClientStream client = new(
            ".", name, PipeDirection.In,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            int connectMilliseconds = (int)Math.Clamp(
                (deadline - DateTime.UtcNow).TotalMilliseconds, 1, 500);
            client.Connect(connectMilliseconds);
        }
        catch (Exception error) when (
            error is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        byte[] reply = new byte[1];
        using CancellationTokenSource cancelled = new();
        Task<int> read = client.ReadAsync(reply, 0, 1, cancelled.Token);
        _ = read.ContinueWith(task => _ = task.Exception,
            TaskContinuationOptions.OnlyOnFaulted);
        try
        {
            return AwaitReply(read, reply, deadline);
        }
        finally
        {
            cancelled.Cancel();
        }
    }

    internal static bool AwaitReply(Task<int> read, byte[] reply, DateTime deadline)
    {
        try
        {
            if (!read.IsCompleted)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    try
                    {
                        int count = read.WaitAsync(remaining)
                            .GetAwaiter().GetResult();
                        return count == 1 && reply[0] == 1;
                    }
                    catch (TimeoutException)
                    {
                        // A reply can complete as the timeout fires.
                    }
                }
                if (!read.IsCompleted)
                {
                    return false;
                }
            }
            return read.GetAwaiter().GetResult() == 1 && reply[0] == 1;
        }
        catch (Exception error) when (
            error is IOException or OperationCanceledException or TimeoutException)
        {
            return false;
        }
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref stopRequested, 1) != 0)
        {
            return;
        }
        stopped.Cancel();
        foreach (Request request in pending.Keys)
        {
            request.Complete(false);
        }
    }

    public void Dispose()
    {
        Stop();
        acceptLoop.GetAwaiter().GetResult();
        stopped.Dispose();
    }

    private NamedPipeServerStream CreateListener() => new(
        name, PipeDirection.Out, NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task AcceptAsync(NamedPipeServerStream first)
    {
        NamedPipeServerStream? listener = first;
        try
        {
            while (!stopped.IsCancellationRequested)
            {
                await listener.WaitForConnectionAsync(stopped.Token)
                    .ConfigureAwait(false);
                NamedPipeServerStream connection = listener;
                listener = CreateListener();
                Request request = new(connection,
                    completed => pending.TryRemove(completed, out _));
                pending.TryAdd(request, 0);
                try
                {
                    if (stopped.IsCancellationRequested || !dispatch(request))
                    {
                        request.Complete(false);
                    }
                }
                catch (Exception)
                {
                    request.Complete(false);
                }
            }
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
        }
        finally
        {
            listener?.Dispose();
        }
    }

    public sealed class Request
    {
        private readonly NamedPipeServerStream connection;
        private readonly Action<Request> completedCallback;
        private readonly object completionGate = new();
        private bool completed;

        internal Request(NamedPipeServerStream connection,
            Action<Request> completedCallback)
        {
            this.connection = connection;
            this.completedCallback = completedCallback;
        }

        /// <returns>Whether this call wrote the reply to its own connection.</returns>
        public bool Complete(bool accepted)
        {
            lock (completionGate)
            {
                if (completed)
                {
                    return false;
                }
                completed = true;
                try
                {
                    connection.WriteByte(accepted ? (byte)1 : (byte)0);
                    return true;
                }
                catch (Exception error) when (
                    error is IOException or ObjectDisposedException)
                {
                    return false;
                }
                finally
                {
                    connection.Dispose();
                    completedCallback(this);
                }
            }
        }
    }
}
