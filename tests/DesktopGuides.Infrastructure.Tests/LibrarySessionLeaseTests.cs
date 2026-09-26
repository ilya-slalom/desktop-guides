using DesktopGuides.Infrastructure.Storage;
using Xunit;

namespace DesktopGuides.Infrastructure.Tests;

public sealed class LibrarySessionLeaseTests
{
    [Fact]
    public async Task SecondSessionWaitsForFirstAndCanCancelItsWait()
    {
        string root = Path.Combine(
            Path.GetTempPath(), $"desktop-guides-lease-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Task<LibrarySessionLease> waiting;
            await using (LibrarySessionLease first =
                await LibrarySessionLease.AcquireAsync(root))
            {
                using CancellationTokenSource canceled = new(
                    TimeSpan.FromMilliseconds(200));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    LibrarySessionLease.AcquireAsync(root, canceled.Token));

                waiting = LibrarySessionLease.AcquireAsync(root);
                await Task.Delay(200);
                Assert.False(waiting.IsCompleted);
            }

            await using LibrarySessionLease second =
                await waiting.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
